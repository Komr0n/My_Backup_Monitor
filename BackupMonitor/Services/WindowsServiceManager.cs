using System;
using System.Diagnostics;
using System.IO;
using System.ServiceProcess;
using System.Runtime.Versioning;
using System.Threading;

namespace BackupMonitor.Services
{
    [SupportedOSPlatform("windows")]
    public class WindowsServiceManager
    {
        private const string ServiceName = "BackupMonitorService";

        public virtual bool IsServiceInstalled()
        {
            try
            {
                using var controller = new ServiceController(ServiceName);
                // Accessing status will throw if service does not exist
                _ = controller.Status;
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        public virtual ServiceControllerStatus? GetServiceStatus()
        {
            if (!IsServiceInstalled()) return null;
            try
            {
                using var controller = new ServiceController(ServiceName);
                controller.Refresh();
                return controller.Status;
            }
            catch (InvalidOperationException) { return null; }
        }

        public virtual bool StartService()
        {
            if (!IsServiceInstalled()) return false;
            try
            {
                using var controller = new ServiceController(ServiceName);
                if (controller.Status == ServiceControllerStatus.Running) return true;
                
                controller.Start();
                controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
                return controller.Status == ServiceControllerStatus.Running;
            }
            catch (Exception) { return false; }
        }

        public virtual bool StopService()
        {
            if (!IsServiceInstalled()) return false;
            try
            {
                using var controller = new ServiceController(ServiceName);
                if (controller.Status == ServiceControllerStatus.Stopped) return true;
                
                controller.Stop();
                controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                return controller.Status == ServiceControllerStatus.Stopped;
            }
            catch (Exception) { return false; }
        }

        public virtual bool InstallService(string serviceExePath)
        {
            if (!File.Exists(serviceExePath)) return false;

            // binPath must be quoted. The space after '=' is important.
            var arguments = $"create {ServiceName} binPath= \"{serviceExePath}\" start= auto DisplayName= \"Backup Monitor Service\"";
            
            var (success, exitCode) = RunProcessAsAdmin("sc.exe", arguments);

            if (success)
            {
                Thread.Sleep(1000); // Give the SCM time to register the service
                return IsServiceInstalled();
            }

            return false;
        }

        public virtual bool UninstallService()
        {
            if (!IsServiceInstalled()) return true;

            if (GetServiceStatus() == ServiceControllerStatus.Running)
            {
                StopService();
            }

            var (success, _) = RunProcessAsAdmin("sc.exe", $"delete {ServiceName}");
            
            if(success)
            {
                Thread.Sleep(1000);
                return !IsServiceInstalled();
            }
            
            return false;
        }

        private (bool success, int exitCode) RunProcessAsAdmin(string fileName, string arguments)
        {
            try
            {
                using var process = new Process
                {
                    StartInfo =
                    {
                        FileName = fileName,
                        Arguments = arguments,
                        Verb = "runas",
                        UseShellExecute = true,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    }
                };
                process.Start();
                process.WaitForExit();
                return (process.ExitCode == 0, process.ExitCode);
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) // Operation cancelled by user
            {
                return (false, ex.NativeErrorCode);
            }
            catch (Exception)
            {
                return (false, -1);
            }
        }

        public virtual string GetServiceStatusText()
        {
            var status = GetServiceStatus();
            return status.HasValue ? status.Value.ToString() : "Not Installed";
        }
    }
}
