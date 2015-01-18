// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Framework.Runtime;
using System.Collections.Generic;
using NuGet.Packaging.Extensions;

namespace NuGet3
{
    public class GraphItem
    {
        public WalkProviderMatch Match { get; set; }
        public IEnumerable<LibraryDependency> Dependencies { get; set; }
    }
}
