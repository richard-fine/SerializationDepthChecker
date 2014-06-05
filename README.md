SerializationDepthChecker
=========================

Simple utility for checking types in a Unity project to make sure there's no serialization trees beyond 8 levels.

**Build:** VS2010 or later, .NET 4. Just open and build the csproj.

**Usage:** SerializationDepthChecker [-u c:\path\to\unity\folder] c:\path\to\project\folder

**Info:** The tool will attempt to load and scan all assemblies in the given project for serializable types, then will explore every possible path from UnityEngine.Object-derived types to see if any paths exceed 8 levels. If it finds any - either due to cycles, or just due to too much nesting - it reports it, with the members it followed along the path.

**Bugs:** Lots. Feel free to submit pull requests. Especially if you know the reflection-only load context, which I don't.

**Author:** Richard "Superpig" Fine, June 2014
