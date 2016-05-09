﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace Microsoft.Build.Logging.StructuredLogger
{
    public class BuildAnalyzer
    {
        private Build build;

        public BuildAnalyzer(Build build)
        {
            this.build = build;
        }

        public static void AnalyzeBuild(Build build)
        {
            var analyzer = new BuildAnalyzer(build);
            analyzer.Analyze();
        }

        private void Analyze()
        {
            build.VisitAllChildren<Target>(t => MarkAsLowRelevanceIfNeeded(t));
            if (!build.Succeeded)
            {
                build.AddChild(new Error { Text = "Build failed." });
            }

            build.VisitAllChildren<CopyTask>(c => AnalyzeFileCopies(c));
            AnalyzeDoubleWrites();
        }

        private void AnalyzeDoubleWrites()
        {
            foreach (var bucket in fileCopySourcesForDestination)
            {
                if (IsDoubleWrite(bucket))
                {
                    var doubleWrites = build.GetOrCreateNodeWithName<Folder>("DoubleWrites");
                    var item = new Item { Text = bucket.Key };
                    doubleWrites.AddChild(item);
                    foreach (var source in bucket.Value)
                    {
                        item.AddChild(new Item { Text = source });
                    }
                }
            }
        }

        private static bool IsDoubleWrite(KeyValuePair<string, HashSet<string>> bucket)
        {
            if (bucket.Value.Count < 2)
            {
                return false;
            }

            if (bucket.Value
                .Select(f => new FileInfo(f))
                .Select(f => f.FullName)
                .Distinct()
                .Count() == 1)
            {
                return false;
            }

            return true;
        }

        private static readonly Dictionary<string, HashSet<string>> fileCopySourcesForDestination = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        private void AnalyzeFileCopies(CopyTask copyTask)
        {
            foreach (var copyOperation in copyTask.FileCopyOperations)
            {
                if (copyOperation.Copied)
                {
                    ProcessCopy(copyOperation.Source, copyOperation.Destination);
                }
            }
        }

        private static void ProcessCopy(string source, string destination)
        {
            HashSet<string> bucket = null;
            if (!fileCopySourcesForDestination.TryGetValue(destination, out bucket))
            {
                bucket = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                fileCopySourcesForDestination.Add(destination, bucket);
            }

            bucket.Add(source);
        }

        private void MarkAsLowRelevanceIfNeeded(Target target)
        {
            if (target.Children.All(c => c is Message))
            {
                target.IsLowRelevance = true;
                foreach (var child in target.Children.OfType<TreeNode>())
                {
                    child.IsLowRelevance = true;
                }
            }
        }

        public static string ByteArrayToHexString(byte[] bytes, int digits = 0)
        {
            if (digits == 0)
            {
                digits = bytes.Length * 2;
            }

            char[] c = new char[digits];
            byte b;
            for (int i = 0; i < digits / 2; i++)
            {
                b = ((byte)(bytes[i] >> 4));
                c[i * 2] = (char)(b > 9 ? b + 87 : b + 0x30);
                b = ((byte)(bytes[i] & 0xF));
                c[i * 2 + 1] = (char)(b > 9 ? b + 87 : b + 0x30);
            }

            return new string(c);
        }

        private static string GetSHA1HashOfFileContents(string filePath)
        {
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var hash = new SHA1Managed())
            {
                var result = hash.ComputeHash(stream);
                return ByteArrayToHexString(result);
            }
        }
    }
}