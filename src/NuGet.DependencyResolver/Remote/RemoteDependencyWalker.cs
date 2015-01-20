// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NuGet.Frameworks;
using NuGet.Packaging.Extensions;
using NuGet.Versioning;
using NuGet.Versioning.Extensions;

namespace NuGet.DependencyResolver
{
    public class RemoteDependencyWalker
    {
        private readonly RemoteWalkContext _context;

        public RemoteDependencyWalker(RemoteWalkContext context)
        {
            _context = context;
        }

        public Task<GraphNode<RemoteResolveResult>> Walk(string name, NuGetVersion version, NuGetFramework framework)
        {
            return CreateGraphNode(new LibraryRange
            {
                Name = name,
                VersionRange = new NuGetVersionRange(version)
            },
            framework);
        }

        private Task<GraphNode<RemoteResolveResult>> CreateGraphNode(LibraryRange libraryRange, NuGetFramework framework)
        {
            return CreateGraphNode(libraryRange, framework, _ => true);
        }

        private async Task<GraphNode<RemoteResolveResult>> CreateGraphNode(LibraryRange libraryRange, NuGetFramework framework, Func<string, bool> predicate)
        {
            var node = new GraphNode<RemoteResolveResult>
            {
                Key = libraryRange,
                Item = await FindLibraryCached(libraryRange, framework),
            };

            if (node.Item == null)
            {
                // Reject null items
                node.Disposition = Disposition.Rejected;
            }
            else
            {
                if (node.Key.VersionRange != null &&
                    node.Key.VersionRange.VersionFloatBehavior != NuGetVersionFloatBehavior.None)
                {
                    lock (_context.FindLibraryCache)
                    {
                        if (!_context.FindLibraryCache.ContainsKey(node.Key))
                        {
                            _context.FindLibraryCache[node.Key] = Task.FromResult(node.Item);
                        }
                    }
                }

                var tasks = new List<Task<GraphNode<RemoteResolveResult>>>();
                var dependencies = node.Item.Data.Dependencies ?? Enumerable.Empty<LibraryDependency>();
                foreach (var dependency in dependencies)
                {
                    if (predicate(dependency.Name))
                    {
                        tasks.Add(CreateGraphNode(dependency.LibraryRange, framework, ChainPredicate(predicate, node.Item, dependency)));
                    }
                }

                while (tasks.Any())
                {
                    var task = await Task.WhenAny(tasks);
                    tasks.Remove(task);
                    var dependencyNode = await task;
                    // Not required for anything
                    dependencyNode.OuterNode = node;
                    node.InnerNodes.Add(dependencyNode);
                }
            }

            return node;
        }

        private Func<string, bool> ChainPredicate(Func<string, bool> predicate, GraphItem<RemoteResolveResult> item, LibraryDependency dependency)
        {
            return name =>
            {
                if (item.Data.Match.Library.Name == name)
                {
                    throw new Exception(string.Format("Circular dependency references not supported. Package '{0}'.", name));
                }

                if (item.Data.Dependencies.Any(d => d != dependency && d.Name == name))
                {
                    return false;
                }

                return predicate(name);
            };
        }

        public Task<GraphItem<RemoteResolveResult>> FindLibraryCached(LibraryRange libraryRange, NuGetFramework framework)
        {
            lock (_context.FindLibraryCache)
            {
                Task<GraphItem<RemoteResolveResult>> task;
                if (!_context.FindLibraryCache.TryGetValue(libraryRange, out task))
                {
                    task = FindLibraryEntry(libraryRange, framework);
                    _context.FindLibraryCache[libraryRange] = task;
                }

                return task;
            }
        }

        private async Task<GraphItem<RemoteResolveResult>> FindLibraryEntry(LibraryRange libraryRange, NuGetFramework framework)
        {
            var match = await FindLibraryMatch(libraryRange, framework);

            if (match == null)
            {
                return null;
            }

            var dependencies = await match.Provider.GetDependencies(match, framework);

            return new GraphItem<RemoteResolveResult>
            {
                Data = new RemoteResolveResult
                {
                    Match = match,
                    Dependencies = dependencies
                },
            };
        }

        private async Task<RemoteMatch> FindLibraryMatch(LibraryRange libraryRange, NuGetFramework framework)
        {
            var projectMatch = await FindProjectMatch(libraryRange.Name, framework);

            if (projectMatch != null)
            {
                return projectMatch;
            }

            if (libraryRange.VersionRange == null)
            {
                return null;
            }

            if (libraryRange.IsGacOrFrameworkReference)
            {
                return null;
            }

            if (libraryRange.VersionRange.VersionFloatBehavior != NuGetVersionFloatBehavior.None)
            {
                // For snapshot dependencies, get the version remotely first.
                var remoteMatch = await FindLibraryByVersion(libraryRange, framework, _context.RemoteLibraryProviders);
                if (remoteMatch == null)
                {
                    // If there was nothing remotely, use the local match (if any)
                    var localMatch = await FindLibraryByVersion(libraryRange, framework, _context.LocalLibraryProviders);
                    return localMatch;
                }
                else
                {
                    // Try to see if the specific version found on the remote exists locally. This avoids any unnecessary
                    // remote access incase we already have it in the cache/local packages folder.
                    var localMatch = await FindLibraryByVersion(remoteMatch.Library, framework, _context.LocalLibraryProviders);

                    if (localMatch != null && localMatch.Library.Version.Equals(remoteMatch.Library.Version))
                    {
                        // If we have a local match, and it matches the version *exactly* then use it.
                        return localMatch;
                    }

                    // We found something locally, but it wasn't an exact match
                    // for the resolved remote match.
                    return remoteMatch;
                }
            }
            else
            {
                // Check for the specific version locally.
                var localMatch = await FindLibraryByVersion(libraryRange, framework, _context.LocalLibraryProviders);

                if (localMatch != null && localMatch.Library.Version.Equals(libraryRange.VersionRange.MinVersion))
                {
                    // We have an exact match so use it.
                    return localMatch;
                }

                // Either we found a local match but it wasn't the exact version, or 
                // we didn't find a local match.
                var remoteMatch = await FindLibraryByVersion(libraryRange, framework, _context.RemoteLibraryProviders);

                if (remoteMatch != null && localMatch == null)
                {
                    // There wasn't any local match for the specified version but there was a remote match.
                    // See if that version exists locally.
                    localMatch = await FindLibraryByVersion(remoteMatch.Library, framework, _context.LocalLibraryProviders);
                }

                if (localMatch != null && remoteMatch != null)
                {
                    // We found a match locally and remotely, so pick the better version
                    // in relation to the specified version.
                    if (libraryRange.VersionRange.IsBetter(
                        current: localMatch.Library.Version,
                        considering: remoteMatch.Library.Version))
                    {
                        return remoteMatch;
                    }
                    else
                    {
                        return localMatch;
                    }
                }

                // Prefer local over remote generally.
                return localMatch ?? remoteMatch;
            }
        }

        private async Task<RemoteMatch> FindProjectMatch(string name, NuGetFramework framework)
        {
            var libraryRange = new LibraryRange
            {
                Name = name
            };

            foreach (var provider in _context.ProjectLibraryProviders)
            {
                var match = await provider.FindLibrary(libraryRange, framework);
                if (match != null)
                {
                    return match;
                }
            }

            return null;
        }

        private async Task<RemoteMatch> FindLibraryByVersion(LibraryRange libraryRange, NuGetFramework framework, IEnumerable<IRemoteDependencyProvider> providers)
        {
            if (libraryRange.VersionRange.VersionFloatBehavior != NuGetVersionFloatBehavior.None)
            {
                // Don't optimize the non http path for floating versions or we'll miss things
                return await FindLibrary(libraryRange, providers, provider => provider.FindLibrary(libraryRange, framework));
            }

            // Try the non http sources first
            var nonHttpMatch = await FindLibrary(libraryRange, providers.Where(p => !p.IsHttp), provider => provider.FindLibrary(libraryRange, framework));

            // If we found an exact match then use it
            if (nonHttpMatch != null && nonHttpMatch.Library.Version.Equals(libraryRange.VersionRange.MinVersion))
            {
                return nonHttpMatch;
            }

            // Otherwise try the http sources
            var httpMatch = await FindLibrary(libraryRange, providers.Where(p => p.IsHttp), provider => provider.FindLibrary(libraryRange, framework));

            // Pick the best match of the 2
            if (libraryRange.VersionRange.IsBetter(
                nonHttpMatch?.Library?.Version,
                httpMatch?.Library.Version))
            {
                return httpMatch;
            }

            return nonHttpMatch;
        }

        private static async Task<RemoteMatch> FindLibrary(
            LibraryRange libraryRange,
            IEnumerable<IRemoteDependencyProvider> providers,
            Func<IRemoteDependencyProvider, Task<RemoteMatch>> action)
        {
            var tasks = new List<Task<RemoteMatch>>();
            foreach (var provider in providers)
            {
                tasks.Add(action(provider));
            }

            RemoteMatch bestMatch = null;
            var matches = await Task.WhenAll(tasks);
            foreach (var match in matches)
            {
                if (libraryRange.VersionRange.IsBetter(
                    current: bestMatch?.Library?.Version,
                    considering: match?.Library?.Version))
                {
                    bestMatch = match;
                }
            }

            return bestMatch;
        }
    }
}