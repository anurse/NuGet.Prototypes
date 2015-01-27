// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.


using System.Collections.Generic;

namespace NuGet.ProjectModel
{
    public interface IProjectResolver
    {
        IEnumerable<string> SearchPaths { get; }

        bool TryResolveProject(string name, out Project project);
    }
}
