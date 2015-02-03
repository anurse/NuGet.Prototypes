// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using NuGet.DependencyResolver;
using NuGet.Frameworks;
using NuGet.LibraryModel;
using NuGet.Packaging.Extensions.Frameworks;

namespace NuGet.ProjectModel
{
    public class ProjectReferenceDependencyProvider : IDependencyProvider
    {
        private readonly IProjectResolver _projectResolver;

        public ProjectReferenceDependencyProvider(IProjectResolver projectResolver)
        {
            _projectResolver = projectResolver;
        }

        public IEnumerable<string> GetAttemptedPaths(FrameworkName targetFramework)
        {
            return _projectResolver.SearchPaths.Select(p => Path.Combine(p, "{name}", "project.json"));
        }

        public LibraryDescription GetDescription(LibraryRange libraryRange, NuGetFramework targetFramework)
        {
            if (libraryRange.IsGacOrFrameworkReference)
            {
                return null;
            }

            string name = libraryRange.Name;

            Project project;

            // Can't find a project file with the name so bail
            if (!_projectResolver.TryResolveProject(name, out project))
            {
                return null;
            }

            // This never returns null
            var targetFrameworkInfo = project.GetTargetFramework(targetFramework);
            var targetFrameworkDependencies = targetFrameworkInfo.Dependencies;

            if (targetFramework.IsDesktop())
            {
                targetFrameworkDependencies.Add(new LibraryDependency
                {
                    LibraryRange = new LibraryRange
                    {
                        Name = "mscorlib",
                        IsGacOrFrameworkReference = true
                    }
                });

                targetFrameworkDependencies.Add(new LibraryDependency
                {
                    LibraryRange = new LibraryRange
                    {
                        Name = "System",
                        IsGacOrFrameworkReference = true
                    }
                });

                targetFrameworkDependencies.Add(new LibraryDependency
                {
                    LibraryRange = new LibraryRange
                    {
                        Name = "System.Core",
                        IsGacOrFrameworkReference = true
                    }
                });

                targetFrameworkDependencies.Add(new LibraryDependency
                {
                    LibraryRange = new LibraryRange
                    {
                        Name = "Microsoft.CSharp",
                        IsGacOrFrameworkReference = true
                    }
                });
            }

            var dependencies = project.Dependencies.Concat(targetFrameworkDependencies).ToList();

            // Mark the library as unresolved if there were specified frameworks
            // and none of them resolved
            bool unresolved = targetFrameworkInfo.FrameworkName == null &&
                              project.TargetFrameworks.Any();

            var description = new LibraryDescription
            {
                LibraryRange = libraryRange,
                Identity = new Library
                {
                    Name = project.Name,
                    Version = project.Version
                },
                Type = LibraryDescriptionTypes.Project,
                Path = project.ProjectFilePath,
                Dependencies = dependencies,
                Resolved = !unresolved
            };

            description.Items["project"] = project;

            return description;
        }
    }
}
