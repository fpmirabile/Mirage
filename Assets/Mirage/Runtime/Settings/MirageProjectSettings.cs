using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mirage.Logging;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Mirage.Settings
{
    internal static class SettingsLoader
    {
        public const string PACKAGE_NAME = "com.miragenet.mirage";

        public static readonly string SettingName = typeof(MirageProjectSettings).FullName;

#if UNITY_EDITOR
        public static readonly string SettingsFolder = $"ProjectSettings/Packages/{PACKAGE_NAME}";
        public static readonly string SettingsPath = $"{SettingsFolder}/{SettingName}.asset";
#else
        public static readonly string SETTINGS_PATH = $"{PACKAGE_NAME}/{SettingName}";
#endif

        // only 1 instance of settings should exist
        private static MirageProjectSettings settingsInstance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void InitializeLoggers()
        {
            MirageProjectSettings settings = Load();
            settings.ApplyLogLevels();
        }

        public static MirageProjectSettings Load()
        {
            if (settingsInstance != null)
            {
                return settingsInstance;
            }

            // load or create settingsInstance
#if UNITY_EDITOR
            EditorLoad();
#else
            PlayerLoad();
#endif

            // add current log levels to loaded settingsInstance
#if UNITY_EDITOR
            AddLogLevelsFromFactory();
#endif

            return settingsInstance;
        }


        static MirageProjectSettings PlayerLoad()
        {
            settingsInstance = Resources.Load<MirageProjectSettings>(SettingsPath);
            return settingsInstance;
        }

#if UNITY_EDITOR
        static MirageProjectSettings EditorLoad()
        {
            string path = SettingsPath;

            if (!File.Exists(path))
            {
                settingsInstance = ScriptableObject.CreateInstance<MirageProjectSettings>();
                EditorSave(settingsInstance, path);
            }
            else
            {
                settingsInstance = EditorLoadExisting<MirageProjectSettings>(path);
            }

            settingsInstance.hideFlags = HideFlags.HideAndDontSave;
            return settingsInstance;
        }
        public static void EditorSave() => EditorSave(settingsInstance, SettingsPath);
        public static void EditorSave<T>(T settings, string path) where T : ScriptableObject
        {
            if (!Directory.Exists(SettingsFolder))
            {
                Directory.CreateDirectory(SettingsFolder);
            }

            try
            {
                UnityEditorInternal.InternalEditorUtility.SaveToSerializedFileAndForget(new Object[] { settings }, path, true);
            }
            catch (System.Exception ex)
            {
                Debug.LogError("Could not save project settings!\n" + ex);
            }
        }

        public static T EditorLoadExisting<T>(string path) where T : ScriptableObject
        {
            T settings = null;

            try
            {
                settings = (T)UnityEditorInternal.InternalEditorUtility.LoadSerializedFileAndForget(path)[0];
            }
            catch (System.Exception)
            {
                Debug.LogWarning("Could not load project settings. Settings will be reset.");
            }

            if (settings == null)
            {
                RemoveFile(path);
                settings = ScriptableObject.CreateInstance<T>();
                EditorSave(settings, path);
            }

            return settings;
        }

        private static void RemoveFile(string path)
        {
            if (!File.Exists(path))
            {
                return;
            }

            FileAttributes attributes = File.GetAttributes(path);
            if (attributes.HasFlag(FileAttributes.ReadOnly))
            {
                File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            }

            File.Delete(path);
        }

        internal static void AddLogLevelsFromFactory()
        {
            var newLevels = new List<MirageProjectSettings.LoggerType>();

            newLevels.AddRange(LogFactory.loggers.Select(kvp => new MirageProjectSettings.LoggerType(kvp.Key, kvp.Value.filterLogType)));

            if (settingsInstance.logLevels == null || settingsInstance.logLevels.Count == 0)
            {
                settingsInstance.logLevels = newLevels;
                settingsInstance.SortLevels();
                EditorSave();
            }
            else
            {
                bool dirty = false;
                foreach (MirageProjectSettings.LoggerType newLevel in newLevels)
                {
                    bool contains = false;

                    foreach (MirageProjectSettings.LoggerType oldLevel in settingsInstance.logLevels)
                    {
                        if (oldLevel.name == newLevel.name)
                        {
                            contains = true;
                            break;
                        }
                    }

                    if (!contains)
                    {
                        settingsInstance.logLevels.Add(newLevel);
                        dirty = true;
                    }
                }

                if (dirty)
                {
                    // Sort in order to keep them in the same place.
                    // It also looks nicer. :)
                    settingsInstance.SortLevels();
                    EditorSave();
                }
            }
        }
#endif
    }

#if UNITY_EDITOR
    /// <summary>
    /// Adds <see cref="MirageProjectSettings"/> to projects settings on build
    /// </summary>
    public static class SettingsBuilder
    {
        static SettingsBuilder()
        {
            ProjectSettingsBuildProcessor.OnBuild += OnProjectSettingsBuild;
        }
        private static void OnProjectSettingsBuild(List<ScriptableObject> list, List<string> names)
        {
            list.Add(SettingsLoader.Load());
            names.Add(SettingsLoader.SettingName);
        }
    }
#endif

    /// <summary>
    /// Mirage Settings
    /// <para>Current just saves Log levels</para>
    /// </summary>
    public class MirageProjectSettings : ScriptableObject
    {
        public List<LoggerType> logLevels = null;

        /// <summary>
        /// Sets <see cref="ILogger.filterLogType"/> in <see cref="LogFactory"/> using <see cref="logLevels"/>
        /// </summary>
        public void ApplyLogLevels()
        {
            for (int i = 0; i < logLevels.Count; i++)
            {
                ILogger logger = LogFactory.GetLogger(logLevels[i].name);
                logger.filterLogType = logLevels[i].level;
            }
        }

        public void SortLevels()
        {
            logLevels = logLevels.OrderBy(x => x.Namespace, StringComparer.Ordinal).ThenBy(x => x.name, StringComparer.Ordinal).ToList();
        }

        private static string GetNameFromFullname(string fullname)
        {
            string[] parts = fullname.Split('.');
            return parts.Last();
        }
        private static string GetNamespaceFromFullname(string fullname)
        {
            string[] parts = fullname.Split('.');
            string space;
            if (parts.Length == 1)
            {
                space = string.Empty;
            }
            else
            {
                space = string.Join(".", parts.Take(parts.Length - 1));
            }

            return space;
        }

        [Serializable]
        public class LoggerType : IEquatable<LoggerType>
        {
            public string name;
            public string Namespace;
            public LogType level;

            public string FullName => $"{Namespace}.{name}";

            public LoggerType(string name, string Namespace, LogType level)
            {
                this.name = name;
                this.Namespace = Namespace;
                this.level = level;
            }
            public LoggerType(string fullname, LogType level)
            {
                Namespace = GetNamespaceFromFullname(fullname);
                name = GetNameFromFullname(fullname);
                this.level = level;
            }

            public bool Equals(LoggerType other)
            {
                return name == other.name && Namespace == other.Namespace && level == other.level;
            }
        }
    }
}

