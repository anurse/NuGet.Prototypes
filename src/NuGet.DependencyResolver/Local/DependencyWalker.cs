// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using NuGet.Frameworks;
using NuGet.LibraryModel;
using NuGet.Versioning;
using NuGet.Versioning.Extensions;

namespace NuGet.DependencyResolver
{
    public class DependencyWalker
    {
        private readonly IEnumerable<IDependencyProvider> _dependencyProviders;

        public DependencyWalker(IEnumerable<IDependencyProvider> dependencyProviders)
        {
            _dependencyProviders = dependencyProviders;
        }

        public GraphNode<LibraryDescription> Walk(string name, NuGetVersion version, NuGetFramework framework)
        {
            var key = new LibraryRange
            {
                Name = name,
                VersionRange = new NuGetVersionRange(version)
            };

            var root = new GraphNode<LibraryDescription>(key);

            var resolvedItems = new Dictionary<LibraryRange, GraphItem<LibraryDescription>>();

            // Recurse through dependencies optimistically, asking resolvers for dependencies
            // based on best match of each encountered dependency
            root.ForEach(node =>
            {
                node.Item = Resolve(resolvedItems, node.Key, framework);
                if (node.Item == null)
                {
                    node.Disposition = Disposition.Rejected;
                    return;
                }

                foreach (var dependency in node.Item.Data.Dependencies)
                {
                    // determine if a child dependency is eclipsed by
                    // a reference on the line leading to this point. this
                    // prevents cyclical dependencies, and also implements the
                    // "nearest wins" rule.

                    var eclipsed = false;
                    for (var scanNode = node;
                         scanNode != null && !eclipsed;
                         scanNode = scanNode.OuterNode)
                    {
                        eclipsed |= scanNode.Key.IsEclipsedBy(dependency.LibraryRange);

                        if (eclipsed)
                        {
                            throw new InvalidOperationException(string.Format("Circular dependency detected {0}.", GetChain(node, dependency)));
                        }

                        foreach (var sideNode in scanNode.InnerNodes)
                        {
                            eclipsed |= sideNode.Key.IsEclipsedBy(dependency.LibraryRange);

                            if (eclipsed)
                            {
                                break;
                            }
                        }
                    }

                    if (!eclipsed)
                    {
                        var innerNode = new GraphNode<LibraryDescription>(dependency.LibraryRange)
                        {
                            OuterNode = node
                        };

                        node.InnerNodes.Add(innerNode);
                    }
                }
            });

            return root;
        }

        private static string GetChain(GraphNode<LibraryDescription> node, LibraryDependency dependency)
        {
            var result = dependency.Name;
            var current = node;

            while (current != null)
            {
                result = current.Key.Name + " -> " + result;
                current = current.OuterNode;
            }

            return result;
        }

        private GraphItem<LibraryDescription> Resolve(
            Dictionary<LibraryRange, GraphItem<LibraryDescription>> resolvedItems,
            LibraryRange library,
            NuGetFramework framework)
        {
            GraphItem<LibraryDescription> item;
            if (resolvedItems.TryGetValue(library, out item))
            {
                return item;
            }

            LibraryDescription hit = null;

            foreach (var dependencyProvider in _dependencyProviders)
            {
                // Skip unsupported library type
                if (!dependencyProvider.SupportsType(library.Type))
                {
                    continue;
                }

                hit = dependencyProvider.GetDescription(library, framework);
                if (hit != null)
                {
                    break;
                }
            }

            if (hit == null)
            {
                resolvedItems[library] = null;
                return null;
            }

            if (resolvedItems.TryGetValue(hit.Identity, out item))
            {
                return item;
            }

            item = new GraphItem<LibraryDescription>(hit.Identity)
            {
                Data = hit
            };

            resolvedItems[library] = item;
            resolvedItems[hit.Identity] = item;
            return item;
        }
    }
}
