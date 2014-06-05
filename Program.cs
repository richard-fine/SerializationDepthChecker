using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace SerializationDepthCheck
{
    internal class Program
    {
        public const string DefaultUnityPath = @"C:\Program Files (x86)\Unity";

        public const int MaxSerializationDepth = 8;

        public static readonly List<Type> BuiltinSerializableTypes = new List<Type>
        {
            typeof (int),
            typeof (bool),
            typeof (float),
            typeof (string),
            typeof (Enum),
            typeof (char),
            // Remaining types are loaded from UnityEngine.dll when starting up
        };

        public static Type UnityObjectType;

        private static Type UnwrapCollectionTypes(Type t)
        {
            if (t.IsArray) return t.GetElementType();

            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>)) return t.GetGenericArguments()[0];

            return t;
        }

        public static bool IsTypeUnitySerializable(Type t)
        {
            t = UnwrapCollectionTypes(t);

            if (t.IsGenericType) return false;

            return BuiltinSerializableTypes.Contains(t) || t.IsSubclassOf(UnityObjectType) ||
                   t.GetCustomAttributesData().Any(d => d.Constructor.DeclaringType == typeof(SerializableAttribute));
        }

        private static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine(@"Usage: SerializationDepthCheck [-u c:\path\to\unity] c:\path\to\project");
                Console.WriteLine();
                return;
            }

            string unityPath = DefaultUnityPath;

            if (args[0] == "-u")
                unityPath = args[1];

            if (!File.Exists(unityPath + @"\Editor\unity.exe"))
            {
                Console.WriteLine("Could not find Unity at " + unityPath + @"\Editor\unity.exe");
                Console.WriteLine("Please specify the correct path to Unity using the -u option.");
                Console.WriteLine();
                return;
            }

            string projectPath = args.Last();
            if (!Directory.Exists(projectPath) || !Directory.Exists(projectPath + @"\Library"))
            {
                Console.WriteLine("Could not find project library at \"{0}\\Library\".", projectPath);
                Console.WriteLine();
                return;
            }

            // Load all Unity assemblies
            UnityObjectType = LoadUnityAssemblies(unityPath);

            IEnumerable<Assembly> projectAssemblies = LoadProjectAssemblies(projectPath);

            List<Type> allProjectTypes = CollectProjectTypes(projectAssemblies);

            // Build list of all dependencies between types
            Dictionary<Type, List<TypeDependency>> dependencies = BuildTypeDependencies(allProjectTypes);

            Console.WriteLine();
            Console.WriteLine("-------------------------------------------------------");
            Console.WriteLine("Beginning walk of dependency graph...");
            Console.WriteLine();

            // Consider every path from each root in turn
            var path = new TypeDependency[MaxSerializationDepth];
            foreach (Type root in allProjectTypes.Where(t => t.IsSubclassOf(UnityObjectType)))
            {
                WalkType(root, 0, path, dependencies);
            }

            Console.WriteLine();
            Console.WriteLine("...done.");
        }

        #region Dependency path walking

        private static void DisplayPath(IEnumerable<TypeDependency> path)
        {
            foreach (TypeDependency entry in path)
            {
                if (entry == null) return;
                Console.WriteLine("{0} {1}.{2}", entry.ToType.FullName, entry.FromType.FullName, entry.Member.Name);
            }
        }

        private static void WalkType(Type root, int level, TypeDependency[] path,
            Dictionary<Type, List<TypeDependency>> dependencies)
        {
            if (level >= path.Length)
            {
                // Found an invalid path
                Console.WriteLine("Serialization depth exceeded by the following member chain:");
                DisplayPath(path);
                Console.WriteLine();
                return;
            }

            List<TypeDependency> rootDeps;
            if (!dependencies.TryGetValue(root, out rootDeps) || rootDeps == null) return;

            foreach (TypeDependency dep in rootDeps)
            {
                path[level] = dep;
                WalkType(path[level].ToType, level + 1, path, dependencies);
            }
            path[level] = null;
        }

        #endregion

        #region Type map

        private static List<Type> CollectProjectTypes(IEnumerable<Assembly> projectAssemblies)
        {
            var allProjectTypes = new List<Type>();

            foreach (Assembly assembly in projectAssemblies)
            {
                try
                {
                    allProjectTypes.AddRange(assembly.GetTypes());
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Couldn't process types from assembly {0}: {1}", assembly.FullName, ex.Message);
                }
            }
            return allProjectTypes;
        }

        private static Dictionary<Type, List<TypeDependency>> BuildTypeDependencies(IEnumerable<Type> allProjectTypes)
        {
            var dependencies = new Dictionary<Type, List<TypeDependency>>();
            foreach (Type type in allProjectTypes)
            {
                if (!IsTypeUnitySerializable(type)) continue;
                List<TypeDependency> td = null;
                foreach (
                    FieldInfo fieldInfo in
                        type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (fieldInfo.IsNotSerialized) continue;

                    Type dstType = UnwrapCollectionTypes(fieldInfo.FieldType);
                    if (dstType.IsSubclassOf(UnityObjectType) || !IsTypeUnitySerializable(dstType)) continue;
                    if (td == null)
                        td = new List<TypeDependency>();
                    td.Add(new TypeDependency(type, dstType, fieldInfo));
                }
                dependencies.Add(type, td);
            }
            return dependencies;
        }

        public class TypeDependency
        {
            public Type FromType;
            public MemberInfo Member;
            public Type ToType;

            public TypeDependency(Type from, Type to, MemberInfo member)
            {
                FromType = from;
                ToType = to;
                Member = member;
            }
        }

        #endregion

        #region Assembly loading

        private static IEnumerable<Assembly> LoadProjectAssemblies(string projectPath)
        {
            var projectAssemblies = new List<Assembly>();

            // Load all project assemblies
            ReflectionOnlyLoadDirectory(projectPath + @"\Assets", projectAssemblies);
            ReflectionOnlyLoadDirectory(projectPath + @"\Library\ScriptAssemblies", projectAssemblies);
            return projectAssemblies;
        }

        private static void ReflectionOnlyLoadDirectory(string path, ICollection<Assembly> projectAssemblies)
        {
            foreach (string file in Directory.GetFiles(path, "*.dll", SearchOption.AllDirectories))
            {
                try
                {
                    Assembly a = Assembly.ReflectionOnlyLoadFrom(file);
                    if (projectAssemblies != null) projectAssemblies.Add(a);
                    Console.WriteLine("Loaded {0}", a.FullName);
                }
                catch (BadImageFormatException)
                {
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Could not load {0}, skipping ({1})", file, ex.Message);
                }
            }
        }

        private static Type LoadUnityAssemblies(string unityPath)
        {
            Assembly unityEngine = Assembly.ReflectionOnlyLoadFrom(unityPath + @"\Editor\Data\Managed\UnityEngine.dll");
            Type result = unityEngine.GetType("UnityEngine.Object");

            foreach (var typename in new[] {"Color","LayerMask","Vector2","Vector3","Vector4","Rect","AnimationCurve","Bounds","Gradient","Quaternion"})
                BuiltinSerializableTypes.Add(unityEngine.GetType("UnityEngine." + typename));

            ReflectionOnlyLoadDirectory(unityPath + @"\Editor\Data\Managed", null);
            ReflectionOnlyLoadDirectory(unityPath + @"\Editor\Data\Mono\lib\mono\2.0", null);

            return result;
        }

        #endregion
    }
}