using System;
using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using Depends.Core;
using Depends.Core.Graph;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Logging;
using Terminal.Gui;

namespace Depends
{
    internal class Program
    {
        public static int Main(string[] args) => CommandLineApplication.Execute<Program>(args);

        [Argument(0, Description = "The project file to analyze. If a project file is not specified, Depends searches the current working directory for a file that has a file extension that ends in proj and uses that file.")]
        // ReSharper disable once UnassignedGetOnlyAutoProperty
        public string Project { get; set; } = Directory.GetCurrentDirectory();

        [Option("-v|--verbosity <LEVEL>", Description = "Sets the verbosity level of the command. Allowed values are Trace, Debug, Information, Warning, Error, Critical, None")]
        // ReSharper disable once UnassignedGetOnlyAutoProperty
        public LogLevel Verbosity { get; }

        [Option("-f|--framework <FRAMEWORK>", Description = "Analyzes for a specific framework. The framework must be defined in the project file.")]
        public string Framework { get; }

        [Option("--package <PACKAGE>", Description = "Analyzes a specific package.")]
        public string Package { get; }

        [Option("--version <PACKAGEVERSION>", Description = "The version of the package to analyze.")]
        public string Version { get; }

        // Following method derived from dotnet-outdated, licensed under MIT
        // MIT License
        //
        // Copyright (c) 2018 Jerrie Pelser
        //
        // Permission is hereby granted, free of charge, to any person obtaining a copy
        // of this software and associated documentation files (the "Software"), to deal
        // in the Software without restriction, including without limitation the rights
        // to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
        // copies of the Software, and to permit persons to whom the Software is
        // furnished to do so, subject to the following conditions:
        //
        // The above copyright notice and this permission notice shall be included in all
        // copies or substantial portions of the Software.
        //
        // THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
        // IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
        // FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
        // AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
        // LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
        // OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
        // SOFTWARE.
        //
        // https://github.com/jerriep/dotnet-outdated/blob/b2c9e99c530a64e246ac529bbdc42ddde19b1e1a/src/DotNetOutdated.Core/Services/ProjectDiscoveryService.cs
        // ReSharper disable once UnusedMember.Local
        private ValidationResult OnValidate()
        {
            if (!(File.Exists(Project) || Directory.Exists(Project)))
            {
                return new ValidationResult("Project path does not exist.");
            }

            var fileAttributes = File.GetAttributes(Project);
            
            // If a directory was passed in, search for a .sln or .proj file
            if (fileAttributes.HasFlag(FileAttributes.Directory))
            {
                // Search for solution(s)
                var solutionFiles = Directory.GetFiles(Project, "*.sln");
                if (solutionFiles.Length == 1)
                {
                    Project = Path.GetFullPath(solutionFiles[0]);
                    return ValidationResult.Success;
                }
                
                if (solutionFiles.Length > 1)
                {
                    return new ValidationResult($"More than one solution file found in working directory.");
                }
                
                // We did not find any solutions, so try and find individual projects
                var projectFiles = Directory.GetFiles(Project, "*.*proj").ToArray();

                if (projectFiles.Length == 1)
                {
                    Project = Path.GetFullPath(projectFiles[0]);
                    return ValidationResult.Success;
                }
                
                if (projectFiles.Length > 1)
                {
                    return new ValidationResult($"More than one project file found in working directory.");
                }

                // At this point the path contains no solutions or projects, so throw an exception
                new ValidationResult($"Unable to find any solution or project files in working directory.");
            }

            Project = Path.GetFullPath(Project);
            return ValidationResult.Success;
        }

        // ReSharper disable once UnusedMember.Local
        private void OnExecute()
        {
            var loggerFactory = LoggerFactory.Create(builder => builder
                .SetMinimumLevel(Verbosity)
                .AddConsole());
            var analyzer = new DependencyAnalyzer(loggerFactory);

            DependencyGraph graph;
            if (!string.IsNullOrEmpty(Package))
            {
                graph = analyzer.Analyze(Package, Version, Framework);
            }
            else if (Path.GetExtension(Project).Equals(".sln", StringComparison.OrdinalIgnoreCase))
            {
                graph = analyzer.AnalyzeSolution(Project, Framework);
            }
            else
            {
                graph = analyzer.Analyze(Project, Framework);
            }

            Application.Init();

            var top = new CustomWindow();

            var left = new FrameView("Dependencies")
            {
                Width = Dim.Percent(50),
                Height = Dim.Fill(1)
            };
            var right = new View()
            {
                X = Pos.Right(left),
                Width = Dim.Fill(),
                Height = Dim.Fill(1)
            };
            var helpText = new Label("Use arrow keys and Tab to move around. Ctrl+D to toggle assembly visibility. Esc to quit.")
            {
                Y = Pos.AnchorEnd(1)
            };

            var runtimeDepends = new FrameView("Runtime depends")
            {
                Width = Dim.Fill(),
                Height = Dim.Percent(33f)
            };
            var packageDepends = new FrameView("Package depends")
            {
                Y = Pos.Bottom(runtimeDepends),
                Width = Dim.Fill(),
                Height = Dim.Percent(50f)
            };
            var reverseDepends = new FrameView("Reverse depends")
            {
                Y = Pos.Bottom(packageDepends),
                Width = Dim.Fill(),
                Height = Dim.Fill()
            };

            var orderedDependencyList = graph.Nodes.OrderBy(x => x.Id).ToImmutableList();
            var dependenciesView = new ListView(orderedDependencyList)
            {
                CanFocus = true,
                AllowsMarking = false
            };
            left.Add(dependenciesView);
            var runtimeDependsView = new ListView(Array.Empty<Node>())
            {
                CanFocus = true,
                AllowsMarking = false
            };
            runtimeDepends.Add(runtimeDependsView);
            var packageDependsView = new ListView(Array.Empty<Node>())
            {
                CanFocus = true,
                AllowsMarking = false
            };
            packageDepends.Add(packageDependsView);
            var reverseDependsView = new ListView(Array.Empty<Node>())
            {
                CanFocus = true,
                AllowsMarking = false
            };
            reverseDepends.Add(reverseDependsView);

            right.Add(runtimeDepends, packageDepends, reverseDepends);
            top.Add(left, right, helpText);
            Application.Top.Add(top);

            top.Dependencies = orderedDependencyList;
            top.VisibleDependencies = orderedDependencyList;
            top.DependenciesView = dependenciesView;

            dependenciesView.SelectedItem = 0;
            UpdateLists();

            dependenciesView.SelectedChanged += UpdateLists;

            Application.Run();

            void UpdateLists()
            {
                var selectedNode = top.VisibleDependencies[dependenciesView.SelectedItem];

                runtimeDependsView.SetSource(graph.Edges.Where(x => x.Start.Equals(selectedNode) && x.End is AssemblyReferenceNode)
                    .Select(x => x.End).ToImmutableList());
                packageDependsView.SetSource(graph.Edges.Where(x => x.Start.Equals(selectedNode) && x.End is PackageReferenceNode)
                    .Select(x => $"{x.End}{(string.IsNullOrEmpty(x.Label) ? string.Empty : " (Wanted: " + x.Label + ")")}").ToImmutableList());
                reverseDependsView.SetSource(graph.Edges.Where(x => x.End.Equals(selectedNode))
                    .Select(x => $"{x.Start}{(string.IsNullOrEmpty(x.Label) ? string.Empty : " (Wanted: " + x.Label + ")")}").ToImmutableList());
            }
        }

        private class CustomWindow : Window
        {
            public CustomWindow() : base("Depends", 0) { }

            public ListView DependenciesView { get; set; }
            public ImmutableList<Node> Dependencies { get; set; }
            public ImmutableList<Node> VisibleDependencies { get; set; }

            private bool _assembliesVisible = true;

            public override bool ProcessKey(KeyEvent keyEvent)
            {
                if (keyEvent.Key == Key.Esc)
                {
                    Application.RequestStop();
                    return true;
                }
                if (keyEvent.Key == Key.ControlD)
                {
                    _assembliesVisible = !_assembliesVisible;

                    VisibleDependencies = _assembliesVisible ?
                        Dependencies :
                        Dependencies.Where(d => !(d is AssemblyReferenceNode)).ToImmutableList();

                    DependenciesView.SetSource(VisibleDependencies);

                    DependenciesView.SelectedItem = 0;
                    return true;
                }

                return base.ProcessKey(keyEvent);
            }
        }
    }
}
