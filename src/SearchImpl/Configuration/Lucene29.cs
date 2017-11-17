﻿using System;
using SenseNet.ContentRepository.Storage.Data;
using SenseNet.Search.Lucene29;

// ReSharper disable RedundantTypeArgumentsOfMethod
namespace SenseNet.Configuration
{
    public class Lucene29 : SnConfig
    {
        private const string SectionName = "sensenet/lucene29";

        internal static LucQuery.ContentQueryExecutionAlgorithm ContentQueryExecutionAlgorithm { get; set; } =
            GetValue<LucQuery.ContentQueryExecutionAlgorithm>(SectionName, "ContentQueryExecutionAlgorithm");

        public static int[] DefaultTopAndGrowth { get; internal set; } =
            ParseDefaultTopAndGrowth(GetValue<string>(SectionName, "DefaultTopAndGrowth", "100,1000,10000,0"));
        private static int[] ParseDefaultTopAndGrowth(string configValue)
        {
            var items = configValue.Split(new [] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            var values = new int[items.Length];

            for (var i = 0; i < items.Length; i++)
            {
                var last = i == items.Length - 1;
                int parsedInt;

                if (int.TryParse(items[i], out parsedInt))
                    values[i] = parsedInt;
                else
                    throw new ConfigurationException("Invalid sequence in the value of 'DefaultTopAndGrowth'. Every value can be positive integer except last, it can be positive integer or zero.");

                if (parsedInt < 0)
                    throw new ConfigurationException("Invalid sequence in the value of 'DefaultTopAndGrowth'. A value cannot less than 0.");

                if (parsedInt == 0)
                {
                    if (!last)
                        throw new ConfigurationException("Invalid sequence in the value of 'DefaultTopAndGrowth'. Only the last value can be 0.");
                }
                else
                {
                    if (i > 0 && parsedInt <= values[i - 1])
                        throw new ConfigurationException("Invalid sequence in the value of 'DefaultTopAndGrowth'. The sequence must be monotonically increasing. Last value can be greater than any other or zero.");
                }
            }
            return values;
        }

        public static int LuceneMergeFactor { get; internal set; } = GetInt(SectionName, "LuceneMergeFactor", 10);
        public static double LuceneRAMBufferSizeMB { get; internal set; } = GetDouble(SectionName, "LuceneRAMBufferSizeMB", 16.0);
        public static int LuceneMaxMergeDocs { get; internal set; } = GetInt(SectionName, "LuceneMaxMergeDocs", int.MaxValue);
        public static int LuceneLockDeleteRetryInterval { get; internal set; } =
            GetInt(SectionName, "LuceneLockDeleteRetryInterval", 60);
        public static int IndexLockFileWaitForRemovedTimeout { get; internal set; } =
            GetInt(SectionName, "IndexLockFileWaitForRemovedTimeout", 120);
        public static string IndexLockFileRemovedNotificationEmail { get; internal set; } = GetString(SectionName,
            "IndexLockFileRemovedNotificationEmail", string.Empty);
    }
}