// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace NuGet.Client
{
    public class NuGetv2PackageFolder : IPackageFeed
    {
        private readonly NuGetv2LocalRepository _repository;
        private readonly IReport _report;

        public string Source { get; }

        public NuGetv2PackageFolder(string physicalPath, IReport report)
        {
            _repository = new NuGetv2LocalRepository(physicalPath);
            _report = report;

            Source = physicalPath;
        }

        public Task<IEnumerable<PackageInfo>> FindPackagesByIdAsync(string id)
        {
            return Task.FromResult(_repository.FindPackagesById(id).Select(p => new PackageInfo
            {
                Id = p.Id,
                Version = p.Version,
                ContentUri = p.ZipPath,
                // This is null
                ManifestContentUri = p.ManifestPath
            }));
        }

        public async Task<Stream> OpenNuspecStreamAsync(PackageInfo package)
        {
            return await PackageUtilities.OpenNuspecStreamFromNupkgAsync(package, OpenNupkgStreamAsync, _report);
        }

        public Task<Stream> OpenNupkgStreamAsync(PackageInfo package)
        {
            return Task.FromResult<Stream>(File.OpenRead(package.ContentUri));
        }
    }
}

