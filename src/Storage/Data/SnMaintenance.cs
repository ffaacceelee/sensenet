﻿using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SenseNet.BackgroundOperations;
using SenseNet.Diagnostics;
using SenseNet.TaskManagement.Core;

namespace SenseNet.ContentRepository.Storage.Data
{
    /// <summary>
    /// Internal service for periodically executing maintenance operations (e.g. cleaning up orphaned binary rows in the database).
    /// All operations are executed when the timer ticks, but they can opt out (skip an iteration) by using a dedicated flag
    /// to avoid parallel execution.
    /// </summary>
    internal class SnMaintenance : ISnService
    {
        private static readonly int TIMER_INTERVAL = 10;
        private static Timer _maintenanceTimer;
        private static int _currentCycle;
        private const string TRACE_PREFIX = "#SnMaintenance> ";

        // ========================================================================================= ISnService implementation

        public bool Start()
        {
            _maintenanceTimer = new Timer(MaintenanceTimerElapsed, null, TIMER_INTERVAL * 1000, TIMER_INTERVAL * 1000);

            return true;
        }

        public void Shutdown()
        {
            if (_maintenanceTimer == null)
                return;

            _maintenanceTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _maintenanceTimer.Dispose();
            _maintenanceTimer = null;
        }

        // ========================================================================================= Timer event handler

        private static void MaintenanceTimerElapsed(object state)
        {
            // Increment the global cycle counter. Different maintenance tasks may
            // rely on this to decide whether they should be executed in the
            // current cycle.
            Interlocked.Increment(ref _currentCycle);
            
            // preventing the counter from overflow
            if (_currentCycle > 100000)
                _currentCycle = 0;

            if (SnTrace.System.Enabled)
                SnTrace.System.Write("CPU: {0}%, RAM: {1} KBytes available (working set: {2} bytes)",
                    CounterManager.GetCPUUsage(),
                    CounterManager.GetAvailableRAM(), 
                    Environment.WorkingSet);

            // start maintenance tasks asychronously
            Task.Run(() => CleanupFiles());
            Task.Run(() => StartADSync());
        }

        // ========================================================================================= Maintenance operations

        // =============================================== Binary cleanup =================================================

        private static bool _fileCleanupIsRunning;
        private const int CLEANUPFILE_CYCLECOUNT = 6; // 1 minute

        /// <summary>
        /// When a content with a binary (e.g. a document) is deleted, the file row itself is not
        /// deleted from the database, it is only detached from the binary property table for performance
        /// reasons. This method cleans up these orphaned rows.
        /// </summary>
        private static void CleanupFiles()
        {
            // skip cleanup, if it is already running
            if (_fileCleanupIsRunning)
                return;

            // skip task if not enough cycles have passed
            if (!IsTaskExecutable(CLEANUPFILE_CYCLECOUNT))
                return;

            _fileCleanupIsRunning = true;

            try
            {
                // preparation: flag rows to delete
                CleanupFilesSetFlag();
                // delete rows one by one to lessen the load on the SQL server
                CleanupFilesDeleteRows();
            }
            finally
            {
                _fileCleanupIsRunning = false;
            }
        }
        /// <summary>
        /// This method only flags orphaned rows for the CleanupFilesDeleteRows 
        /// method that will actually delete the rows from the database.
        /// </summary>
        private static void CleanupFilesSetFlag()
        {
            try
            {
                SnTrace.Database.Write(TRACE_PREFIX + "Cleanup files: setting the IsDeleted flag...");
                BlobStorage.CleanupFilesSetFlag();
            }
            catch (Exception ex)
            {
                SnLog.WriteWarning("Error in file cleanup set flag background process. " + ex, EventId.RepositoryRuntime);
            }
        }
        /// <summary>
        /// This method deletes orphaned rows from the database physically.
        /// </summary>
        private static void CleanupFilesDeleteRows()
        {
            var deleteCount = 0;

            try
            {
                SnTrace.Database.Write(TRACE_PREFIX + "Cleanup files: deleting rows...");

                // keep deleting orphaned binary rows while there are any
                while (BlobStorage.CleanupFiles())
                {
                    deleteCount++;
                }

                if (deleteCount > 0)
                    SnLog.WriteInformation(string.Format("{0} orphaned rows were deleted from the binary table during cleanup.", deleteCount), EventId.RepositoryRuntime);
            }
            catch (Exception ex)
            {
                SnLog.WriteWarning("Error in file cleanup background process. " + ex, EventId.RepositoryRuntime);
            }
        }

        // =============================================== AD sync =========================================================

        private const int ADSYNC_CYCLECOUNT = 5; // 40 seconds
        private static readonly string ADSyncSettingsName = "SyncAD2Portal";
        private static bool? _adsyncAvailable;

        private static void StartADSync()
        {
            if (!_adsyncAvailable.HasValue)
            {
                _adsyncAvailable = Settings.IsSettingsAvailable(ADSyncSettingsName);
                SnLog.WriteInformation("Active Directory synch feature is " + (_adsyncAvailable.Value ? string.Empty : "not ") + "available.");
            }
            if (!_adsyncAvailable.Value)
                return;

            // skip check if not enough cycles have passed or the feature is not enabled
            if (!IsTaskExecutable(ADSYNC_CYCLECOUNT) || !Settings.GetValue(ADSyncSettingsName, "Enabled", null, false))
                return;

            if (!AdSyncTimeArrived())
                return;

            var requestData = new RegisterTaskRequest
            {
                Type = "SyncAD2Portal",
                Title = "SyncAD2Portal",
                Priority = TaskPriority.Immediately,
                AppId = Settings.GetValue(SnTaskManager.Settings.SETTINGSNAME, SnTaskManager.Settings.TASKMANAGEMENTAPPID, null, "SenseNet"),
                TaskData = JsonConvert.SerializeObject(new { SiteUrl = Settings.GetValue<string>(SnTaskManager.Settings.SETTINGSNAME, SnTaskManager.Settings.TASKMANAGEMENTAPPLICATIONURL) }),
                Tag = string.Empty,
                FinalizeUrl = "/odata.svc/('Root')/Ad2PortalSyncFinalizer"
            };

            // Fire and forget: we do not need the result of the register operation.
            // (we have to start a task here instead of calling RegisterTaskAsync 
            // directly because the asp.net sync context callback would fail)
            Task.Run(() => SnTaskManager.RegisterTaskAsync(requestData));
        }

        private static bool AdSyncTimeArrived()
        {
            var now = DateTime.UtcNow;
            var dayOffSetInMinutes = now.Hour*60 + now.Minute;

            var frequency = Math.Max(0, Settings.GetValue(ADSyncSettingsName, "Scheduling.Frequency", null, 0));
            if (frequency > 0)
                return dayOffSetInMinutes % frequency < 1;

            var exactTimeVal = Settings.GetValue(ADSyncSettingsName, "Scheduling.ExactTime", null, string.Empty);
            TimeSpan exactTime;
            if (TimeSpan.TryParse(exactTimeVal, out exactTime) && exactTime > TimeSpan.MinValue)
                return dayOffSetInMinutes == Convert.ToInt64(Math.Truncate(exactTime.TotalMinutes));

            return false;
        }

        // ========================================================================================= Helper methods

        private static bool IsTaskExecutable(int cycleLength)
        {
            // We are in the correct cycle if the current timer cycle is divisible
            // by the cycle length defined by the particular task.
            return _currentCycle%cycleLength == 0;
        }
    }
}