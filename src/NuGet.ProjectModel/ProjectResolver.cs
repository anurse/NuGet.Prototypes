// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace NuGet.ProjectModel
{
    public class ProjectResolver : IProjectResolver
    {
        private HashSet<string> _searchPaths = new HashSet<string>();
        private Dictionary<string, ProjectInformation> _projects = new Dictionary<string, ProjectInformation>();

        public ProjectResolver(string projectPath, string rootPath)
        {
            // We could find all project.json files in the search paths up front here
            Initialize(projectPath, rootPath);
        }

        public IEnumerable<string> SearchPaths
        {
            get
            {
                return _searchPaths;
            }
        }

        public bool TryResolveProject(string name, out Project project)
        {
            project = null;

            ProjectInformation projectInfo;
            if (_projects.TryGetValue(name, out projectInfo))
            {
                project = projectInfo.Project;
            }

            return false;
        }

        private void Initialize(string projectPath, string rootPath)
        {
            _searchPaths.Add(Path.GetDirectoryName(projectPath));

            GlobalSettings global;

            if (GlobalSettings.TryGetGlobalSettings(rootPath, out global))
            {
                foreach (var sourcePath in global.ProjectPaths)
                {
                    _searchPaths.Add(Path.Combine(rootPath, sourcePath));
                }
            }

            // Resolve all of the potential projects
            foreach (var searchPath in _searchPaths)
            {
                var directory = new DirectoryInfo(searchPath);

                if (!directory.Exists)
                {
                    continue;
                }

                foreach (var projectDirectory in directory.EnumerateDirectories())
                {
                    // The name of the folder is the project
                    _projects[projectDirectory.Name] = new ProjectInformation
                    {
                        Name = projectDirectory.Name,
                        FullPath = projectDirectory.FullName
                    };
                }
            }
        }

        public static string ResolveRootDirectory(string projectPath)
        {
            var di = new DirectoryInfo(projectPath);

            while (di.Parent != null)
            {
                var globalJsonPath = Path.Combine(di.FullName, GlobalSettings.GlobalFileName);

                if (File.Exists(globalJsonPath))
                {
                    return di.FullName;
                }

                di = di.Parent;
            }

            // If we don't find any files then make the project folder the root
            return projectPath;
        }

        private class ProjectInformation
        {
            private Project _project;
            private bool _initialized;
            private object _lockObj = new object();

            public string Name { get; set; }

            public string FullPath { get; set; }

            public Project Project
            {
                get
                {
                    return LazyInitializer.EnsureInitialized(ref _project, ref _initialized, ref _lockObj, () =>
                    {
                        Project project;
                        ProjectReader.TryReadProject(FullPath, out project);
                        return project;
                    });
                }
            }
        }
    }
}
