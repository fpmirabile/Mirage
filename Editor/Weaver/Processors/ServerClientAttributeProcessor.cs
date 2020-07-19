using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Mirror.Weaver
{
    /// <summary>
    /// processes SyncVars, Cmds, Rpcs, etc. of NetworkBehaviours
    /// </summary>
    static class ServerClientAttributeProcessor
    {
        public static void ProcessMethodAttributes(MethodDefinition md)
        {
            foreach (CustomAttribute attr in md.CustomAttributes)
            {
                switch (attr.Constructor.DeclaringType.ToString())
                {
                    case "Mirror.ServerAttribute":
                        InjectGuard(md, attr, Weaver.NetworkBehaviourIsServer, "[Server] function '" + md.FullName + "' called on client");
                        break;
                    case "Mirror.ClientAttribute":
                        InjectGuard(md, attr, Weaver.NetworkBehaviourIsClient, "[Client] function '" + md.FullName + "' called on server");
                        break;
                    case "Mirror.HasAuthorityAttribute":
                        InjectGuard(md, attr, Weaver.NetworkBehaviourHasAuthority, "[Has Authority] function '" + md.FullName + "' called on player without authority");
                        break;
                    case "Mirror.LocalPlayerAttribute":
                        InjectGuard(md, attr, Weaver.NetworkBehaviourIsLocalPlayer, "[Local Player] function '" + md.FullName + "' called on nonlocal player");
                        break;
                    default:
                        break;
                }
            }
        }

        static void InjectGuard(MethodDefinition md, CustomAttribute attribute, MethodReference predicate, string message)
        {
            bool throwError = attribute.GetField("error", true);

            if (!Weaver.IsNetworkBehaviour(md.DeclaringType))
            {
                Weaver.Error($"{attribute.AttributeType.Name} method {md.Name} must be declared in a NetworkBehaviour", md);
                return;
            }
            ILProcessor worker = md.Body.GetILProcessor();
            Instruction top = md.Body.Instructions[0];

            worker.InsertBefore(top, worker.Create(OpCodes.Ldarg_0));
            worker.InsertBefore(top, worker.Create(OpCodes.Call, predicate));
            worker.InsertBefore(top, worker.Create(OpCodes.Brtrue, top));
            if (throwError)
            {
                worker.InsertBefore(top, worker.Create(OpCodes.Ldstr, message));
                worker.InsertBefore(top, worker.Create(OpCodes.Newobj, Weaver.MethodInvocationExceptionConstructor));
                worker.InsertBefore(top, worker.Create(OpCodes.Throw));
            }
            InjectGuardParameters(md, worker, top);
            InjectGuardReturnValue(md, worker, top);
            worker.InsertBefore(top, worker.Create(OpCodes.Ret));
        }

        // this is required to early-out from a function with "ref" or "out" parameters
        static void InjectGuardParameters(MethodDefinition md, ILProcessor worker, Instruction top)
        {
            int offset = md.Resolve().IsStatic ? 0 : 1;
            for (int index = 0; index < md.Parameters.Count; index++)
            {
                ParameterDefinition param = md.Parameters[index];
                if (param.IsOut)
                {
                    TypeReference elementType = param.ParameterType.GetElementType();
                    if (elementType.IsPrimitive)
                    {
                        worker.InsertBefore(top, worker.Create(OpCodes.Ldarg, index + offset));
                        worker.InsertBefore(top, worker.Create(OpCodes.Ldc_I4_0));
                        worker.InsertBefore(top, worker.Create(OpCodes.Stind_I4));
                    }
                    else
                    {
                        md.Body.Variables.Add(new VariableDefinition(elementType));
                        md.Body.InitLocals = true;

                        worker.InsertBefore(top, worker.Create(OpCodes.Ldarg, index + offset));
                        worker.InsertBefore(top, worker.Create(OpCodes.Ldloca_S, (byte)(md.Body.Variables.Count - 1)));
                        worker.InsertBefore(top, worker.Create(OpCodes.Initobj, elementType));
                        worker.InsertBefore(top, worker.Create(OpCodes.Ldloc, md.Body.Variables.Count - 1));
                        worker.InsertBefore(top, worker.Create(OpCodes.Stobj, elementType));
                    }
                }
            }
        }

        // this is required to early-out from a function with a return value.
        static void InjectGuardReturnValue(MethodDefinition md, ILProcessor worker, Instruction top)
        {
            if (md.ReturnType.FullName != Weaver.voidType.FullName)
            {
                if (md.ReturnType.IsPrimitive)
                {
                    worker.InsertBefore(top, worker.Create(OpCodes.Ldc_I4_0));
                }
                else
                {
                    md.Body.Variables.Add(new VariableDefinition(md.ReturnType));
                    md.Body.InitLocals = true;

                    worker.InsertBefore(top, worker.Create(OpCodes.Ldloca_S, (byte)(md.Body.Variables.Count - 1)));
                    worker.InsertBefore(top, worker.Create(OpCodes.Initobj, md.ReturnType));
                    worker.InsertBefore(top, worker.Create(OpCodes.Ldloc, md.Body.Variables.Count - 1));
                }
            }
        }
    }
}