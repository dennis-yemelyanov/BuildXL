// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";

import * as BuildXLSdk from "Sdk.BuildXL";
import * as Managed from "Sdk.Managed";
import * as Deployment from "Sdk.Deployment";
import { NetFx } from "Sdk.BuildXL";

namespace ServicePipDaemon {

    @@public
    export const dll = !BuildXLSdk.isDaemonToolingEnabled ? undefined : BuildXLSdk.library({
        assemblyName: "Tool.ServicePipDaemon",
        rootNamespace: "Tool.ServicePipDaemon",        
        sources: globR(d`.`, "*.cs"),
        references:[            
            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
            importFrom("BuildXL.Cache.ContentStore").UtilitiesCore.dll,
            importFrom("BuildXL.Utilities.Instrumentation").Common.dll,
            importFrom("BuildXL.Utilities.Instrumentation").Tracing.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Ipc.dll,
            importFrom("BuildXL.Utilities").Ipc.Providers.dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Storage.dll,

            importFrom("Newtonsoft.Json").pkg,
            ...BuildXLSdk.systemThreadingTasksDataflowPackageReference,
        ],
        internalsVisibleTo: [
            "Test.Tool.DropDaemon",
        ]
    });
}
