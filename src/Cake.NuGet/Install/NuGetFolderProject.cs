﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cake.Core.Configuration;
using Cake.Core.Diagnostics;
using Cake.Core.IO;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.ProjectManagement;
using NuGet.Protocol.Core.Types;
using PackageReference = Cake.Core.Packaging.PackageReference;
using PackageType = Cake.Core.Packaging.PackageType;

namespace Cake.NuGet.Install
{
    internal sealed class NugetFolderProject : FolderNuGetProject
    {
        private readonly ISet<PackageIdentity> _installedPackages;
        private readonly IFileSystem _fileSystem;
        private readonly INuGetContentResolver _contentResolver;
        private readonly ICakeConfiguration _config;
        private readonly ICakeLog _log;
        private readonly PackagePathResolver _pathResolver;

        private static readonly ISet<string> _blackListedPackages = new HashSet<string>(new[]
        {
            "Cake.Common",
            "Cake.Core"
        }, StringComparer.OrdinalIgnoreCase);

        public NugetFolderProject(
            IFileSystem fileSystem,
            INuGetContentResolver contentResolver,
            ICakeConfiguration config,
            ICakeLog log,
            PackagePathResolver pathResolver,
            string root) : base(root, pathResolver)
        {
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _contentResolver = contentResolver ?? throw new ArgumentNullException(nameof(contentResolver));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
            _installedPackages = new HashSet<PackageIdentity>();
        }

        public override Task<bool> InstallPackageAsync(PackageIdentity packageIdentity, DownloadResourceResult downloadResourceResult,
            INuGetProjectContext nuGetProjectContext, CancellationToken token)
        {
            _installedPackages.Add(packageIdentity);

            if (_fileSystem.Exist(new DirectoryPath(_pathResolver.GetInstallPath(packageIdentity))))
            {
                _log.Debug("Package {0} has already been installed.", packageIdentity.ToString());
                return Task.FromResult(true);
            }
            return base.InstallPackageAsync(packageIdentity, downloadResourceResult, nuGetProjectContext, token);
        }

        public IReadOnlyCollection<IFile> GetFiles(DirectoryPath directoryPath, PackageReference packageReference, PackageType type)
        {
            bool loadDependencies;
            if (packageReference.Parameters.ContainsKey("LoadDependencies"))
            {
                bool.TryParse(packageReference.Parameters["LoadDependencies"].FirstOrDefault() ?? bool.TrueString, out loadDependencies);
            }
            else
            {
                bool.TryParse(_config.GetValue(Constants.NuGet.LoadDependencies) ?? bool.FalseString, out loadDependencies);
            }

            var files = new List<IFile>();
            var package = _installedPackages.First(p => p.Id.Equals(packageReference.Package, StringComparison.OrdinalIgnoreCase));
            var installPath = new DirectoryPath(_pathResolver.GetInstallPath(package));

            if (!_fileSystem.Exist(installPath))
            {
                _log.Warning("Package {0} is not installed.", packageReference.Package);
                return Array.Empty<IFile>();
            }

            files.AddRange(_contentResolver.GetFiles(installPath, packageReference, type));

            if (loadDependencies)
            {
                foreach (var dependency in _installedPackages
                    .Where(p => !p.Id.Equals(packageReference.Package, StringComparison.OrdinalIgnoreCase)))
                {
                    if (_blackListedPackages.Contains(dependency.Id))
                    {
                        _log.Warning("Package {0} depends on package {1}. Will not load this dependency...",
                            packageReference.Package, dependency.ToString());
                        continue;
                    }

                    var dependencyInstallPath = new DirectoryPath(_pathResolver.GetInstallPath(package));

                    if (!_fileSystem.Exist(dependencyInstallPath))
                    {
                        _log.Warning("Package {0} is not installed.", dependency.Id);
                        continue;
                    }

                    files.AddRange(_contentResolver.GetFiles(dependencyInstallPath, packageReference, type));
                }
            }

            return files;
        }
    }
}
