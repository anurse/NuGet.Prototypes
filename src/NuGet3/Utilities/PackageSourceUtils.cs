// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using NuGet.Configuration;
using NuGet.Client;

namespace NuGet3
{
    public static class PackageSourceUtils
    {
        public static List<PackageSource> GetEffectivePackageSources(IPackageSourceProvider sourceProvider,
            IEnumerable<string> sources, IEnumerable<string> fallbackSources)
        {
            var allSources = sourceProvider.LoadPackageSources();
            var enabledSources = sources.Any() ?
                Enumerable.Empty<PackageSource>() :
                allSources.Where(s => s.IsEnabled);

            var addedSources = sources.Concat(fallbackSources).Select(
                value => allSources.FirstOrDefault(source => CorrectName(value, source)) ?? new PackageSource(value));

            return enabledSources.Concat(addedSources).Distinct().ToList();
        }

        public static IPackageFeed CreatePackageFeed(PackageSource source, bool noCache, bool ignoreFailedSources,
            Reports reports)
        {
            if (new Uri(source.Source).IsFile)
            {
                return PackageFolderFactory.CreatePackageFolderFromPath(source.Source, ignoreFailedSources, reports);
            }
            else
            {
                return new NuGetv2Feed(
                    source.Source,
                    source.UserName,
                    source.Password,
                    noCache,
                    ignoreFailedSources,
                    reports);
            }
        }

        private static bool CorrectName(string value, PackageSource source)
        {
            return source.Name.Equals(value, StringComparison.CurrentCultureIgnoreCase) ||
                source.Source.Equals(value, StringComparison.OrdinalIgnoreCase);
        }
    }
}