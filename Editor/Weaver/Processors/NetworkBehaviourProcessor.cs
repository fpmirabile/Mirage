using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Mirror.Weaver
{
    public enum RemoteCallType
    {
        ServerRpc,
        ClientRpc,
        SyncEvent
    }


    /// <summary>
    /// processes SyncVars, Cmds, Rpcs, etc. of NetworkBehaviours
    /// </summary>
    class NetworkBehaviourProcessor
    {
        readonly List<FieldDefinition> syncVars = new List<FieldDefinition>();
        readonly List<FieldDefinition> syncObjects = new List<FieldDefinition>();
        // <SyncVarField,NetIdField>
        readonly Dictionary<FieldDefinition, FieldDefinition> syncVarNetIds = new Dictionary<FieldDefinition, FieldDefinition>();
        readonly List<CmdResult> serverRpcs = new List<CmdResult>();
        readonly List<ClientRpcResult> clientRpcs = new List<ClientRpcResult>();
        readonly List<EventDefinition> eventRpcs = new List<EventDefinition>();
        readonly List<MethodDefinition> serverRpcSkeletonFuncs = new List<MethodDefinition>();
        readonly List<MethodDefinition> clientRpcSkeletonFuncs = new List<MethodDefinition>();
        readonly List<MethodDefinition> eventRpcInvocationFuncs = new List<MethodDefinition>();

        readonly TypeDefinition netBehaviourSubclass;

        public struct CmdResult
        {
            public MethodDefinition method;
            public bool requireAuthority;
        }

        public struct ClientRpcResult
        {
            public MethodDefinition method;
            public Client target;
            public bool excludeOwner;
        }

        public NetworkBehaviourProcessor(TypeDefinition td)
        {
            Weaver.DLog(td, "NetworkBehaviourProcessor");
            netBehaviourSubclass = td;
        }

        public void Process()
        {
            if (netBehaviourSubclass.HasGenericParameters)
            {
                Weaver.Error($"{netBehaviourSubclass.Name} cannot have generic parameters", netBehaviourSubclass);
                return;
            }
            Weaver.DLog(netBehaviourSubclass, "Process Start");
            MarkAsProcessed(netBehaviourSubclass);
            SyncVarProcessor.ProcessSyncVars(netBehaviourSubclass, syncVars, syncObjects, syncVarNetIds);

            ProcessMethods();

            SyncEventProcessor.ProcessEvents(netBehaviourSubclass, eventRpcs, eventRpcInvocationFuncs);
            if (Weaver.WeavingFailed)
            {
                return;
            }
            GenerateConstants();

            GenerateSerialization();
            if (Weaver.WeavingFailed)
            {
                return;
            }

            GenerateDeSerialization();
            Weaver.DLog(netBehaviourSubclass, "Process Done");
        }

        /*
        generates code like:
            if (!obj.netIdentity.client.active)
              Debug.LogError((object) "ServerRpc function CmdRespawn called on server.");

            which is used in InvokeCmd, InvokeRpc, etc.
        */
        public static void WriteClientActiveCheck(ILProcessor worker, string mdName, Instruction label, string errString)
        {
            // client active check
            worker.Append(worker.Create(OpCodes.Ldarg_0));
            worker.Append(worker.Create(OpCodes.Call, Weaver.NetworkBehaviourGetIdentity));
            worker.Append(worker.Create(OpCodes.Call, Weaver.NetworkIdentityGetClient));
            worker.Append(worker.Create(OpCodes.Call, Weaver.NetworkClientGetActive));
            worker.Append(worker.Create(OpCodes.Brtrue, label));

            worker.Append(worker.Create(OpCodes.Ldstr, errString + " " + mdName + " called on server."));
            worker.Append(worker.Create(OpCodes.Call, Weaver.logErrorReference));
            worker.Append(worker.Create(OpCodes.Ret));
            worker.Append(label);
        }
        /*
        generates code like:
            if (!obj.netIdentity.server.active)
              Debug.LogError((object) "ServerRpc CmdMsgWhisper called on client.");
        */
        public static void WriteServerActiveCheck(ILProcessor worker, string mdName, Instruction label, string errString)
        {
            // server active check
            worker.Append(worker.Create(OpCodes.Ldarg_0));
            worker.Append(worker.Create(OpCodes.Call, Weaver.NetworkBehaviourGetIdentity));
            worker.Append(worker.Create(OpCodes.Call, Weaver.NetworkIdentityGetServer));
            worker.Append(worker.Create(OpCodes.Call, Weaver.NetworkServerGetActive));
            worker.Append(worker.Create(OpCodes.Brtrue, label));

            worker.Append(worker.Create(OpCodes.Ldstr, errString + " " + mdName + " called on client."));
            worker.Append(worker.Create(OpCodes.Call, Weaver.logErrorReference));
            worker.Append(worker.Create(OpCodes.Ret));
            worker.Append(label);
        }

        public static void WriteSetupLocals(ILProcessor worker)
        {
            worker.Body.InitLocals = true;
            worker.Body.Variables.Add(new VariableDefinition(Weaver.PooledNetworkWriterType));
        }

        public static void WriteCreateWriter(ILProcessor worker)
        {
            // create writer
            worker.Append(worker.Create(OpCodes.Call, Weaver.GetPooledWriterReference));
            worker.Append(worker.Create(OpCodes.Stloc_0));
        }

        public static void WriteRecycleWriter(ILProcessor worker)
        {
            // NetworkWriterPool.Recycle(writer);
            worker.Append(worker.Create(OpCodes.Ldloc_0));
            worker.Append(worker.Create(OpCodes.Call, Weaver.RecycleWriterReference));
        }

        public static bool WriteArguments(ILProcessor worker, MethodDefinition method, RemoteCallType callType)
        {
            // write each argument
            // example result
            /*
            writer.WritePackedInt32(someNumber);
            writer.WriteNetworkIdentity(someTarget);
             */

            bool skipFirst = callType == RemoteCallType.ClientRpc
                && RpcProcessor.HasNetworkConnectionParameter(method);

            // arg of calling  function, arg 0 is "this" so start counting at 1
            int argNum = 1;
            foreach (ParameterDefinition param in method.Parameters)
            {
                // NetworkConnection is not sent via the NetworkWriter so skip it here
                // skip first for NetworkConnection in TargetRpc
                if (argNum == 1 && skipFirst)
                {
                    argNum += 1;
                    continue;
                }
                // skip SenderConnection in ServerRpc
                if (IsNetworkConnection(param.ParameterType))
                {
                    argNum += 1;
                    continue;
                }

                MethodReference writeFunc = Writers.GetWriteFunc(param.ParameterType);
                if (writeFunc == null)
                {
                    Weaver.Error($"{method.Name} has invalid parameter {param}", method);
                    return false;
                }

                // use built-in writer func on writer object
                // NetworkWriter object
                worker.Append(worker.Create(OpCodes.Ldloc_0));
                // add argument to call
                worker.Append(worker.Create(OpCodes.Ldarg, argNum));
                // call writer extension method
                worker.Append(worker.Create(OpCodes.Call, writeFunc));
                argNum += 1;
            }
            return true;
        }

        #region mark / check type as processed
        public const string ProcessedFunctionName = "MirrorProcessed";

        // by adding an empty MirrorProcessed() function
        public static bool WasProcessed(TypeDefinition td)
        {
            return td.GetMethod(ProcessedFunctionName) != null;
        }

        public static void MarkAsProcessed(TypeDefinition td)
        {
            if (!WasProcessed(td))
            {
                var versionMethod = new MethodDefinition(ProcessedFunctionName, MethodAttributes.Private, Weaver.voidType);
                ILProcessor worker = versionMethod.Body.GetILProcessor();
                worker.Append(worker.Create(OpCodes.Ret));
                td.Methods.Add(versionMethod);
            }
        }
        #endregion

        void GenerateConstants()
        {
            if (serverRpcs.Count == 0 && clientRpcs.Count == 0 && eventRpcs.Count == 0 && syncObjects.Count == 0)
                return;

            Weaver.DLog(netBehaviourSubclass, "  GenerateConstants ");

            // find static constructor
            MethodDefinition cctor = netBehaviourSubclass.GetMethod(".cctor");
            bool cctorFound = cctor != null;
            if (cctor != null)
            {
                // remove the return opcode from end of function. will add our own later.
                if (cctor.Body.Instructions.Count != 0)
                {
                    Instruction retInstr = cctor.Body.Instructions[cctor.Body.Instructions.Count - 1];
                    if (retInstr.OpCode == OpCodes.Ret)
                    {
                        cctor.Body.Instructions.RemoveAt(cctor.Body.Instructions.Count - 1);
                    }
                    else
                    {
                        Weaver.Error($"{netBehaviourSubclass.Name} has invalid class constructor", cctor);
                        return;
                    }
                }
            }
            else
            {
                // make one!
                cctor = new MethodDefinition(".cctor", MethodAttributes.Private |
                        MethodAttributes.HideBySig |
                        MethodAttributes.SpecialName |
                        MethodAttributes.RTSpecialName |
                        MethodAttributes.Static,
                        Weaver.voidType);
            }

            // find instance constructor
            MethodDefinition ctor = netBehaviourSubclass.GetMethod(".ctor");

            if (ctor == null)
            {
                Weaver.Error($"{netBehaviourSubclass.Name} has invalid constructor", netBehaviourSubclass);
                return;
            }

            Instruction ret = ctor.Body.Instructions[ctor.Body.Instructions.Count - 1];
            if (ret.OpCode == OpCodes.Ret)
            {
                ctor.Body.Instructions.RemoveAt(ctor.Body.Instructions.Count - 1);
            }
            else
            {
                Weaver.Error($"{netBehaviourSubclass.Name} has invalid constructor", ctor);
                return;
            }

            // TODO: find out if the order below matters. If it doesn't split code below into 2 functions
            ILProcessor ctorWorker = ctor.Body.GetILProcessor();
            ILProcessor cctorWorker = cctor.Body.GetILProcessor();

            for (int i = 0; i < serverRpcs.Count; ++i)
            {
                CmdResult cmdResult = serverRpcs[i];
                GenerateRegisterServerRpcDelegate(cctorWorker, Weaver.registerServerRpcDelegateReference, serverRpcSkeletonFuncs[i], cmdResult);
            }

            for (int i = 0; i < clientRpcs.Count; ++i)
            {
                ClientRpcResult clientRpcResult = clientRpcs[i];
                GenerateRegisterRemoteDelegate(cctorWorker, Weaver.registerRpcDelegateReference, clientRpcSkeletonFuncs[i], clientRpcResult.method.Name);
            }

            for (int i = 0; i < eventRpcs.Count; ++i)
            {
                GenerateRegisterRemoteDelegate(cctorWorker, Weaver.registerEventDelegateReference, eventRpcInvocationFuncs[i], eventRpcs[i].Name);
            }

            foreach (FieldDefinition fd in syncObjects)
            {
                SyncObjectInitializer.GenerateSyncObjectInitializer(ctorWorker, fd);
            }

            cctorWorker.Append(cctorWorker.Create(OpCodes.Ret));
            if (!cctorFound)
            {
                netBehaviourSubclass.Methods.Add(cctor);
            }

            // finish ctor
            ctorWorker.Append(ctorWorker.Create(OpCodes.Ret));

            // in case class had no cctor, it might have BeforeFieldInit, so injected cctor would be called too late
            netBehaviourSubclass.Attributes &= ~TypeAttributes.BeforeFieldInit;
        }

        /*
            // This generates code like:
            NetworkBehaviour.RegisterServerRpcDelegate(base.GetType(), "CmdThrust", new NetworkBehaviour.CmdDelegate(ShipControl.InvokeCmdCmdThrust));
        */
        void GenerateRegisterRemoteDelegate(ILProcessor worker, MethodReference registerMethod, MethodDefinition func, string cmdName)
        {
            worker.Append(worker.Create(OpCodes.Ldtoken, netBehaviourSubclass));
            worker.Append(worker.Create(OpCodes.Call, Weaver.getTypeFromHandleReference));
            worker.Append(worker.Create(OpCodes.Ldstr, cmdName));
            worker.Append(worker.Create(OpCodes.Ldnull));
            worker.Append(worker.Create(OpCodes.Ldftn, func));

            worker.Append(worker.Create(OpCodes.Newobj, Weaver.CmdDelegateConstructor));
            //
            worker.Append(worker.Create(OpCodes.Call, registerMethod));
        }

        void GenerateRegisterServerRpcDelegate(ILProcessor worker, MethodReference registerMethod, MethodDefinition func, CmdResult cmdResult)
        {
            string cmdName = cmdResult.method.Name;
            bool requireAuthority = cmdResult.requireAuthority;

            worker.Append(worker.Create(OpCodes.Ldtoken, netBehaviourSubclass));
            worker.Append(worker.Create(OpCodes.Call, Weaver.getTypeFromHandleReference));
            worker.Append(worker.Create(OpCodes.Ldstr, cmdName));
            worker.Append(worker.Create(OpCodes.Ldnull));
            worker.Append(worker.Create(OpCodes.Ldftn, func));

            worker.Append(worker.Create(OpCodes.Newobj, Weaver.CmdDelegateConstructor));

            worker.Append(worker.Create(requireAuthority ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0));

            worker.Append(worker.Create(OpCodes.Call, registerMethod));
        }

        void GenerateSerialization()
        {
            Weaver.DLog(netBehaviourSubclass, "  GenerateSerialization");

            const string SerializeMethodName = "SerializeSyncVars";
            if (netBehaviourSubclass.GetMethod(SerializeMethodName) != null)
                return;

            if (syncVars.Count == 0)
            {
                // no synvars,  no need for custom OnSerialize
                return;
            }

            var serialize = new MethodDefinition(SerializeMethodName,
                    MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
                    Weaver.boolType);

            serialize.Parameters.Add(new ParameterDefinition("writer", ParameterAttributes.None, Weaver.CurrentAssembly.MainModule.ImportReference(Weaver.NetworkWriterType)));
            serialize.Parameters.Add(new ParameterDefinition("forceAll", ParameterAttributes.None, Weaver.boolType));
            ILProcessor worker = serialize.Body.GetILProcessor();

            serialize.Body.InitLocals = true;

            // loc_0,  this local variable is to determine if any variable was dirty
            var dirtyLocal = new VariableDefinition(Weaver.boolType);
            serialize.Body.Variables.Add(dirtyLocal);

            MethodReference baseSerialize = Resolvers.ResolveMethodInParents(netBehaviourSubclass.BaseType, Weaver.CurrentAssembly, SerializeMethodName);
            if (baseSerialize != null)
            {
                // base
                worker.Append(worker.Create(OpCodes.Ldarg_0));
                // writer
                worker.Append(worker.Create(OpCodes.Ldarg_1));
                // forceAll
                worker.Append(worker.Create(OpCodes.Ldarg_2));
                worker.Append(worker.Create(OpCodes.Call, baseSerialize));
                // set dirtyLocal to result of base.OnSerialize()
                worker.Append(worker.Create(OpCodes.Stloc_0));
            }

            // Generates: if (forceAll);
            Instruction initialStateLabel = worker.Create(OpCodes.Nop);
            // forceAll
            worker.Append(worker.Create(OpCodes.Ldarg_2));
            worker.Append(worker.Create(OpCodes.Brfalse, initialStateLabel));

            foreach (FieldDefinition syncVar in syncVars)
            {
                // Generates a writer call for each sync variable
                // writer
                worker.Append(worker.Create(OpCodes.Ldarg_1));
                // this
                worker.Append(worker.Create(OpCodes.Ldarg_0));
                worker.Append(worker.Create(OpCodes.Ldfld, syncVar));
                MethodReference writeFunc = Writers.GetWriteFunc(syncVar.FieldType);
                if (writeFunc != null)
                {
                    worker.Append(worker.Create(OpCodes.Call, writeFunc));
                }
                else
                {
                    Weaver.Error($"{syncVar.Name} has unsupported type. Use a supported Mirror type instead", syncVar);
                    return;
                }
            }

            // always return true if forceAll

            // Generates: return true
            worker.Append(worker.Create(OpCodes.Ldc_I4_1));
            worker.Append(worker.Create(OpCodes.Ret));

            // Generates: end if (forceAll);
            worker.Append(initialStateLabel);

            // write dirty bits before the data fields
            // Generates: writer.WritePackedUInt64 (base.get_syncVarDirtyBits ());
            // writer
            worker.Append(worker.Create(OpCodes.Ldarg_1));
            // base
            worker.Append(worker.Create(OpCodes.Ldarg_0));
            worker.Append(worker.Create(OpCodes.Call, Weaver.NetworkBehaviourDirtyBitsReference));
            worker.Append(worker.Create(OpCodes.Call, Writers.GetWriteFunc(Weaver.uint64Type)));

            // generate a writer call for any dirty variable in this class

            // start at number of syncvars in parent
            int dirtyBit = Weaver.GetSyncVarStart(netBehaviourSubclass.BaseType.FullName);
            foreach (FieldDefinition syncVar in syncVars)
            {
                Instruction varLabel = worker.Create(OpCodes.Nop);

                // Generates: if ((base.get_syncVarDirtyBits() & 1uL) != 0uL)
                // base
                worker.Append(worker.Create(OpCodes.Ldarg_0));
                worker.Append(worker.Create(OpCodes.Call, Weaver.NetworkBehaviourDirtyBitsReference));
                // 8 bytes = long
                worker.Append(worker.Create(OpCodes.Ldc_I8, 1L << dirtyBit));
                worker.Append(worker.Create(OpCodes.And));
                worker.Append(worker.Create(OpCodes.Brfalse, varLabel));

                // Generates a call to the writer for that field
                // writer
                worker.Append(worker.Create(OpCodes.Ldarg_1));
                // base
                worker.Append(worker.Create(OpCodes.Ldarg_0));
                worker.Append(worker.Create(OpCodes.Ldfld, syncVar));

                MethodReference writeFunc = Writers.GetWriteFunc(syncVar.FieldType);
                if (writeFunc != null)
                {
                    worker.Append(worker.Create(OpCodes.Call, writeFunc));
                }
                else
                {
                    Weaver.Error($"{syncVar.Name} has unsupported type. Use a supported Mirror type instead", syncVar);
                    return;
                }

                // something was dirty
                worker.Append(worker.Create(OpCodes.Ldc_I4_1));
                // set dirtyLocal to true
                worker.Append(worker.Create(OpCodes.Stloc_0));

                worker.Append(varLabel);
                dirtyBit += 1;
            }

            // generate: return dirtyLocal
            worker.Append(worker.Create(OpCodes.Ldloc_0));
            worker.Append(worker.Create(OpCodes.Ret));
            netBehaviourSubclass.Methods.Add(serialize);
        }

        void DeserializeField(FieldDefinition syncVar, ILProcessor worker, MethodDefinition deserialize)
        {
            // check for Hook function
            MethodDefinition hookMethod = SyncVarProcessor.GetHookMethod(syncVar);

            if (IsNetworkIdentityField(syncVar))
            {
                DeserializeNetworkIdentityField(syncVar, worker, deserialize, hookMethod);
            }
            else
            {
                DeserializeNormalField(syncVar, worker, deserialize, hookMethod);
            }
        }

        /// <summary>
        /// Is the field a NetworkIdentity or GameObject
        /// </summary>
        /// <param name="syncVar"></param>
        /// <returns></returns>
        static bool IsNetworkIdentityField(FieldDefinition syncVar)
        {
            return syncVar.FieldType.FullName == Weaver.gameObjectType.FullName ||
                   syncVar.FieldType.FullName == Weaver.NetworkIdentityType.FullName;
        }

        /// <summary>
        /// [SyncVar] GameObject/NetworkIdentity?
        /// </summary>
        /// <param name="syncVar"></param>
        /// <param name="worker"></param>
        /// <param name="deserialize"></param>
        /// <param name="initialState"></param>
        /// <param name="hookResult"></param>
        void DeserializeNetworkIdentityField(FieldDefinition syncVar, ILProcessor worker, MethodDefinition deserialize, MethodDefinition hookMethod)
        {
            /*
            Generates code like:
               uint oldNetId = ___qNetId;
               // returns GetSyncVarGameObject(___qNetId)
               GameObject oldSyncVar = syncvar.getter;
               ___qNetId = reader.ReadPackedUInt32();
               if (!SyncVarEqual(oldNetId, ref ___goNetId))
               {
                   // getter returns GetSyncVarGameObject(___qNetId)
                   OnSetQ(oldSyncVar, syncvar.getter);
               }
            */

            // GameObject/NetworkIdentity SyncVar:
            //   OnSerialize sends writer.Write(go);
            //   OnDeserialize reads to __netId manually so we can use
            //     lookups in the getter (so it still works if objects
            //     move in and out of range repeatedly)
            FieldDefinition netIdField = syncVarNetIds[syncVar];

            // uint oldNetId = ___qNetId;
            var oldNetId = new VariableDefinition(Weaver.uint32Type);
            deserialize.Body.Variables.Add(oldNetId);
            worker.Append(worker.Create(OpCodes.Ldarg_0));
            worker.Append(worker.Create(OpCodes.Ldfld, netIdField));
            worker.Append(worker.Create(OpCodes.Stloc, oldNetId));

            // GameObject/NetworkIdentity oldSyncVar = syncvar.getter;
            var oldSyncVar = new VariableDefinition(syncVar.FieldType);
            deserialize.Body.Variables.Add(oldSyncVar);
            worker.Append(worker.Create(OpCodes.Ldarg_0));
            worker.Append(worker.Create(OpCodes.Ldfld, syncVar));
            worker.Append(worker.Create(OpCodes.Stloc, oldSyncVar));

            // read id and store in netId field BEFORE calling the hook
            // -> this makes way more sense. by definition, the hook is
            //    supposed to be called after it was changed. not before.
            // -> setting it BEFORE calling the hook fixes the following bug:
            //    https://github.com/vis2k/Mirror/issues/1151 in host mode
            //    where the value during the Hook call would call Cmds on
            //    the host server, and they would all happen and compare
            //    values BEFORE the hook even returned and hence BEFORE the
            //    actual value was even set.
            // put 'this.' onto stack for 'this.netId' below
            worker.Append(worker.Create(OpCodes.Ldarg_0));
            // reader. for 'reader.Read()' below
            worker.Append(worker.Create(OpCodes.Ldarg_1));
            // Read()
            worker.Append(worker.Create(OpCodes.Call, Readers.GetReadFunc(Weaver.uint32Type)));
            // netId
            worker.Append(worker.Create(OpCodes.Stfld, netIdField));

            if (hookMethod != null)
            {
                // call Hook(this.GetSyncVarGameObject/NetworkIdentity(reader.ReadPackedUInt32()))
                // because we send/receive the netID, not the GameObject/NetworkIdentity
                // but only if SyncVar changed. otherwise a client would
                // get hook calls for all initial values, even if they
                // didn't change from the default values on the client.
                // see also: https://github.com/vis2k/Mirror/issues/1278

                // IMPORTANT: for GameObjects/NetworkIdentities we usually
                //            use SyncVarGameObjectEqual to compare equality.
                //            in this case however, we can just use
                //            SyncVarEqual with the two uint netIds.
                //            => this is easier weaver code because we don't
                //               have to get the GameObject/NetworkIdentity
                //               from the uint netId
                //            => this is faster because we void one
                //               GetComponent call for GameObjects to get
                //               their NetworkIdentity when comparing.

                // Generates: if (!SyncVarEqual);
                Instruction syncVarEqualLabel = worker.Create(OpCodes.Nop);

                // 'this.' for 'this.SyncVarEqual'
                worker.Append(worker.Create(OpCodes.Ldarg_0));
                // 'oldNetId'
                worker.Append(worker.Create(OpCodes.Ldloc, oldNetId));
                // 'ref this.__netId'
                worker.Append(worker.Create(OpCodes.Ldarg_0));
                worker.Append(worker.Create(OpCodes.Ldflda, netIdField));
                // call the function
                var syncVarEqualGm = new GenericInstanceMethod(Weaver.syncVarEqualReference);
                syncVarEqualGm.GenericArguments.Add(netIdField.FieldType);
                worker.Append(worker.Create(OpCodes.Call, syncVarEqualGm));
                worker.Append(worker.Create(OpCodes.Brtrue, syncVarEqualLabel));

                // call the hook
                // Generates: OnValueChanged(oldValue, this.syncVar);
                SyncVarProcessor.WriteCallHookMethodUsingField(worker, hookMethod, oldSyncVar, syncVar);

                // Generates: end if (!SyncVarEqual);
                worker.Append(syncVarEqualLabel);
            }
        }

        /// <summary>
        /// [SyncVar] int/float/struct/etc.?
        /// </summary>
        /// <param name="syncVar"></param>
        /// <param name="serWorker"></param>
        /// <param name="deserialize"></param>
        /// <param name="initialState"></param>
        /// <param name="hookResult"></param>
        void DeserializeNormalField(FieldDefinition syncVar, ILProcessor serWorker, MethodDefinition deserialize, MethodDefinition hookMethod)
        {
            /*
             Generates code like:
                // for hook
                int oldValue = a;
                Networka = reader.ReadPackedInt32();
                if (!SyncVarEqual(oldValue, ref a))
                {
                    OnSetA(oldValue, Networka);
                }
             */

            MethodReference readFunc = Readers.GetReadFunc(syncVar.FieldType);
            if (readFunc == null)
            {
                Weaver.Error($"{syncVar.Name} has unsupported type. Use a supported Mirror type instead", syncVar);
                return;
            }

            // T oldValue = value;
            var oldValue = new VariableDefinition(syncVar.FieldType);
            deserialize.Body.Variables.Add(oldValue);
            serWorker.Append(serWorker.Create(OpCodes.Ldarg_0));
            serWorker.Append(serWorker.Create(OpCodes.Ldfld, syncVar));
            serWorker.Append(serWorker.Create(OpCodes.Stloc, oldValue));

            // read value and store in syncvar BEFORE calling the hook
            // -> this makes way more sense. by definition, the hook is
            //    supposed to be called after it was changed. not before.
            // -> setting it BEFORE calling the hook fixes the following bug:
            //    https://github.com/vis2k/Mirror/issues/1151 in host mode
            //    where the value during the Hook call would call Cmds on
            //    the host server, and they would all happen and compare
            //    values BEFORE the hook even returned and hence BEFORE the
            //    actual value was even set.
            // put 'this.' onto stack for 'this.syncvar' below
            serWorker.Append(serWorker.Create(OpCodes.Ldarg_0));
            // reader. for 'reader.Read()' below
            serWorker.Append(serWorker.Create(OpCodes.Ldarg_1));
            // reader.Read()
            serWorker.Append(serWorker.Create(OpCodes.Call, readFunc));
            // syncvar
            serWorker.Append(serWorker.Create(OpCodes.Stfld, syncVar));

            if (hookMethod != null)
            {
                // call hook
                // but only if SyncVar changed. otherwise a client would
                // get hook calls for all initial values, even if they
                // didn't change from the default values on the client.
                // see also: https://github.com/vis2k/Mirror/issues/1278

                // Generates: if (!SyncVarEqual);
                Instruction syncVarEqualLabel = serWorker.Create(OpCodes.Nop);

                // 'this.' for 'this.SyncVarEqual'
                serWorker.Append(serWorker.Create(OpCodes.Ldarg_0));
                // 'oldValue'
                serWorker.Append(serWorker.Create(OpCodes.Ldloc, oldValue));
                // 'ref this.syncVar'
                serWorker.Append(serWorker.Create(OpCodes.Ldarg_0));
                serWorker.Append(serWorker.Create(OpCodes.Ldflda, syncVar));
                // call the function
                var syncVarEqualGm = new GenericInstanceMethod(Weaver.syncVarEqualReference);
                syncVarEqualGm.GenericArguments.Add(syncVar.FieldType);
                serWorker.Append(serWorker.Create(OpCodes.Call, syncVarEqualGm));
                serWorker.Append(serWorker.Create(OpCodes.Brtrue, syncVarEqualLabel));

                // call the hook
                // Generates: OnValueChanged(oldValue, this.syncVar);
                SyncVarProcessor.WriteCallHookMethodUsingField(serWorker, hookMethod, oldValue, syncVar);

                // Generates: end if (!SyncVarEqual);
                serWorker.Append(syncVarEqualLabel);
            }
        }

        void GenerateDeSerialization()
        {
            Weaver.DLog(netBehaviourSubclass, "  GenerateDeSerialization");

            const string DeserializeMethodName = "DeserializeSyncVars";
            if (netBehaviourSubclass.GetMethod(DeserializeMethodName) != null)
                return;

            if (syncVars.Count == 0)
            {
                // no synvars,  no need for custom OnDeserialize
                return;
            }

            var serialize = new MethodDefinition(DeserializeMethodName,
                    MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
                    Weaver.voidType);

            serialize.Parameters.Add(new ParameterDefinition("reader", ParameterAttributes.None, Weaver.CurrentAssembly.MainModule.ImportReference(Weaver.NetworkReaderType)));
            serialize.Parameters.Add(new ParameterDefinition("initialState", ParameterAttributes.None, Weaver.boolType));
            ILProcessor serWorker = serialize.Body.GetILProcessor();
            // setup local for dirty bits
            serialize.Body.InitLocals = true;
            var dirtyBitsLocal = new VariableDefinition(Weaver.int64Type);
            serialize.Body.Variables.Add(dirtyBitsLocal);

            MethodReference baseDeserialize = Resolvers.ResolveMethodInParents(netBehaviourSubclass.BaseType, Weaver.CurrentAssembly, DeserializeMethodName);
            if (baseDeserialize != null)
            {
                // base
                serWorker.Append(serWorker.Create(OpCodes.Ldarg_0));
                // reader
                serWorker.Append(serWorker.Create(OpCodes.Ldarg_1));
                // initialState
                serWorker.Append(serWorker.Create(OpCodes.Ldarg_2));
                serWorker.Append(serWorker.Create(OpCodes.Call, baseDeserialize));
            }

            // Generates: if (initialState);
            Instruction initialStateLabel = serWorker.Create(OpCodes.Nop);

            serWorker.Append(serWorker.Create(OpCodes.Ldarg_2));
            serWorker.Append(serWorker.Create(OpCodes.Brfalse, initialStateLabel));

            foreach (FieldDefinition syncVar in syncVars)
            {
                DeserializeField(syncVar, serWorker, serialize);
            }

            serWorker.Append(serWorker.Create(OpCodes.Ret));

            // Generates: end if (initialState);
            serWorker.Append(initialStateLabel);

            // get dirty bits
            serWorker.Append(serWorker.Create(OpCodes.Ldarg_1));
            serWorker.Append(serWorker.Create(OpCodes.Call, Readers.GetReadFunc(Weaver.uint64Type)));
            serWorker.Append(serWorker.Create(OpCodes.Stloc_0));

            // conditionally read each syncvar
            // start at number of syncvars in parent
            int dirtyBit = Weaver.GetSyncVarStart(netBehaviourSubclass.BaseType.FullName);
            foreach (FieldDefinition syncVar in syncVars)
            {
                Instruction varLabel = serWorker.Create(OpCodes.Nop);

                // check if dirty bit is set
                serWorker.Append(serWorker.Create(OpCodes.Ldloc_0));
                serWorker.Append(serWorker.Create(OpCodes.Ldc_I8, 1L << dirtyBit));
                serWorker.Append(serWorker.Create(OpCodes.And));
                serWorker.Append(serWorker.Create(OpCodes.Brfalse, varLabel));

                DeserializeField(syncVar, serWorker, serialize);

                serWorker.Append(varLabel);
                dirtyBit += 1;
            }

            serWorker.Append(serWorker.Create(OpCodes.Ret));
            netBehaviourSubclass.Methods.Add(serialize);
        }

        public static bool ReadArguments(MethodDefinition method, ILProcessor worker, bool skipFirst)
        {
            // read each argument
            // example result
            /*
            CallCmdDoSomething(reader.ReadPackedInt32(), reader.ReadNetworkIdentity());
             */

            // arg of calling  function, arg 0 is "this" so start counting at 1
            int argNum = 1;
            foreach (ParameterDefinition param in method.Parameters)
            {
                // NetworkConnection is not sent via the NetworkWriter so skip it here
                // skip first for NetworkConnection in TargetRpc
                if (argNum == 1 && skipFirst)
                {
                    argNum += 1;
                    continue;
                }
                // skip SenderConnection in ServerRpc
                if (IsNetworkConnection(param.ParameterType))
                {
                    argNum += 1;
                    continue;
                }

                MethodReference readFunc = Readers.GetReadFunc(param.ParameterType);

                if (readFunc == null)
                {
                    Weaver.Error($"{method.Name} has invalid parameter {param}.  Unsupported type {param.ParameterType},  use a supported Mirror type instead", method);
                    return false;
                }

                worker.Append(worker.Create(OpCodes.Ldarg_1));
                worker.Append(worker.Create(OpCodes.Call, readFunc));

                // conversion.. is this needed?
                if (param.ParameterType.FullName == Weaver.singleType.FullName)
                {
                    worker.Append(worker.Create(OpCodes.Conv_R4));
                }
                else if (param.ParameterType.FullName == Weaver.doubleType.FullName)
                {
                    worker.Append(worker.Create(OpCodes.Conv_R8));
                }
            }
            return true;
        }

        public static void AddInvokeParameters(ICollection<ParameterDefinition> collection)
        {
            collection.Add(new ParameterDefinition("obj", ParameterAttributes.None, Weaver.CurrentAssembly.MainModule.ImportReference(Weaver.NetworkBehaviourType)));
            collection.Add(new ParameterDefinition("reader", ParameterAttributes.None, Weaver.CurrentAssembly.MainModule.ImportReference(Weaver.NetworkReaderType)));
            // senderConnection is only used for ServerRpcs but NetworkBehaviour.CmdDelegate is used for all remote calls
            collection.Add(new ParameterDefinition("senderConnection", ParameterAttributes.None, Weaver.CurrentAssembly.MainModule.ImportReference(Weaver.INetworkConnectionType)));
        }

        public static bool ProcessMethodsValidateFunction(MethodReference md)
        {
            if (md.ReturnType.FullName == Weaver.IEnumeratorType.FullName)
            {
                Weaver.Error($"{md.Name} cannot be a coroutine", md);
                return false;
            }
            if (md.ReturnType.FullName != Weaver.voidType.FullName)
            {
                Weaver.Error($"{md.Name} cannot return a value.  Make it void instead", md);
                return false;
            }
            if (md.HasGenericParameters)
            {
                Weaver.Error($"{md.Name} cannot have generic parameters", md);
                return false;
            }
            return true;
        }

        public static bool ProcessMethodsValidateParameters(MethodReference method, RemoteCallType callType)
        {
            for (int i = 0; i < method.Parameters.Count; ++i)
            {
                ParameterDefinition param = method.Parameters[i];

                bool valid = ValidateParameter(method, param, callType, i == 0);

                if (!valid)
                {
                    return false;
                }
            }
            return true;
        }

        static bool ValidateParameter(MethodReference method, ParameterDefinition param, RemoteCallType callType, bool firstParam)
        {
            if (param.IsOut)
            {
                Weaver.Error($"{method.Name} cannot have out parameters", method);
                return false;
            }

            if (IsNetworkConnection(param.ParameterType))
            {
                if (callType == RemoteCallType.ClientRpc && firstParam)
                {
                    // perfectly fine,  target rpc can receive a network connection as first parameter
                    return true;
                }

                if (callType == RemoteCallType.ServerRpc)
                {
                    return true;
                }

                Weaver.Error($"{method.Name} has invalid parameter {param}, Cannot pass NetworkConnections", method);
                return false;
            }

            if (param.IsOptional)
            {
                Weaver.Error($"{method.Name} cannot have optional parameters", method);
                return false;
            }

            return true;
        }

        public static bool IsNetworkConnection(TypeReference type)
        {
            return type.Resolve().ImplementsInterface(Weaver.INetworkConnectionType);
        }

        void ProcessMethods()
        {
            var names = new HashSet<string>();

            // copy the list of methods because we will be adding methods in the loop
            var methods = new List<MethodDefinition>(netBehaviourSubclass.Methods);
            // find ServerRpc and RPC functions
            foreach (MethodDefinition md in methods)
            {
                foreach (CustomAttribute ca in md.CustomAttributes)
                {
                    if (ca.AttributeType.FullName == Weaver.ServerRpcType.FullName)
                    {
                        ProcessServerRpc(names, md, ca);
                        break;
                    }

                    if (ca.AttributeType.FullName == Weaver.ClientRpcType.FullName)
                    {
                        ProcessClientRpc(names, md, ca);
                        break;
                    }
                }
            }
        }

        void ProcessClientRpc(HashSet<string> names, MethodDefinition md, CustomAttribute clientRpcAttr)
        {
            if (!RpcProcessor.Validate(md, clientRpcAttr))
                return;

            if (names.Contains(md.Name))
            {
                Weaver.Error($"Duplicate ClientRpc name {md.Name}", md);
                return;
            }

            Client clientTarget = clientRpcAttr.GetField("target", Client.Observers);
            bool excludeOwner = clientRpcAttr.GetField("excludeOwner", false);

            names.Add(md.Name);
            clientRpcs.Add(new ClientRpcResult
            {
                method = md,
                target = clientTarget,
                excludeOwner = excludeOwner
            });

            MethodDefinition userCodeFunc = RpcProcessor.GenerateStub(md, clientRpcAttr);

            MethodDefinition skeletonFunc = RpcProcessor.GenerateSkeleton(md, userCodeFunc, clientRpcAttr);
            if (skeletonFunc != null)
            {
                clientRpcSkeletonFuncs.Add(skeletonFunc);
            }
        }

        void ProcessServerRpc(HashSet<string> names, MethodDefinition md, CustomAttribute serverRpcAttr)
        {
            if (!ServerRpcProcessor.Validate(md))
                return;

            if (names.Contains(md.Name))
            {
                Weaver.Error($"Duplicate ServerRpc name {md.Name}", md);
                return;
            }

            bool requireAuthority = serverRpcAttr.GetField("requireAuthority", false);

            names.Add(md.Name);
            serverRpcs.Add(new CmdResult
            {
                method = md,
                requireAuthority = requireAuthority
            });

            MethodDefinition userCodeFunc = ServerRpcProcessor.GenerateStub(md, serverRpcAttr);

            MethodDefinition skeletonFunc = ServerRpcProcessor.GenerateSkeleton(md, userCodeFunc);
            if (skeletonFunc != null)
            {
                serverRpcSkeletonFuncs.Add(skeletonFunc);
            }
        }
    }
}