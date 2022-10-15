using System;
using System.Runtime.InteropServices;

using System.IO;

using System.Reflection;
using System.Configuration.Install;

namespace Palissimo.WinFUSE
{

    public class SelfServiceInstaller
    {
        //private readonly string installUtil = Path.Combine(Path.GetDirectoryName(typeof(string).Assembly.Location), "InstallUtil.exe");

        //private AppDomain dom = AppDomain.CreateDomain("temp");

        private readonly string exePath = Assembly.GetExecutingAssembly().Location;

        public bool InstallMe()
        {
            try
            {
                // dom.ExecuteAssembly(installUtil, null, new string[] { "/LogFile=", "/LogToConsole=true", exePath });
                ManagedInstallerClass.InstallHelper( new string[] { exePath } );

            }
            catch
            {
                return false;
            }
            return true;
        }

        public bool UnInstallMe()
        {
            try
            {
                //dom.ExecuteAssembly(installUtil, null, new string[] { "/u", "/LogFile=", "/LogToConsole=true", exePath });
                ManagedInstallerClass.InstallHelper( new string[] { "/u", exePath } );
            }
            catch
            {
                return false;
            }
            return true;
        }
    }



    public class MyServiceInstaller
    {

        [DllImport("advapi32.dll")]
        public static extern void CloseServiceHandle(IntPtr SCHANDLE);

        [DllImport("Advapi32.dll")]
        public static extern IntPtr CreateService(IntPtr SC_HANDLE, string lpSvcName, string lpDisplayName, int dwDesiredAccess, int dwServiceType, int dwStartType, int dwErrorControl, string lpPathName, string lpLoadOrderGroup, int lpdwTagId, string lpDependencies, string lpServiceStartName, string lpPassword);

        [DllImport("advapi32.dll")]
        public static extern int DeleteService(IntPtr SVHANDLE);

        [DllImport("advapi32.dll")]
        public static extern IntPtr OpenSCManager(string lpMachineName, string lpSCDB, int scParameter);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern IntPtr OpenService(IntPtr SCHANDLE, string lpSvcName, int dwNumServiceArgs);

        [DllImport("advapi32.dll")]
        public static extern int StartService(IntPtr SVHANDLE, int dwNumServiceArgs, string lpServiceArgVectors);
      
        public bool InstallService(string svcPath, string svcName, string svcDispName)
        {
            int scParameter = 2;
            int dwServiceType = 0x10;
            int dwErrorControl = 1;
            int num4 = 0xf0000;
            int num5 = 1;
            int num6 = 2;
            int num7 = 4;
            int num8 = 8;
            int num9 = 0x10;
            int num10 = 0x20;
            int num11 = 0x40;
            int num12 = 0x80;
            int num13 = 0x100;
            int dwDesiredAccess = num4 | num5 | num6 | num7 | num8 | num9 | num10 | num11 | num12 | num13;
            int dwStartType = 2;
            try
            {
                IntPtr ptr = OpenSCManager(null, null, scParameter);
                if (ptr.ToInt32() != 0)
                {
                    IntPtr sVHANDLE = CreateService(ptr, svcName, svcDispName, dwDesiredAccess, dwServiceType, dwStartType, dwErrorControl, svcPath, null, 0, null, null, null);
                    if (sVHANDLE.ToInt32() == 0)
                    {
                        CloseServiceHandle(ptr);
                        return false;
                    }
                    if (StartService(sVHANDLE, 0, null) == 0)
                    {
                        return false;
                    }
                    CloseServiceHandle(ptr);
                    return true;
                }
            }
            catch (Exception exception)
            {
                throw exception;
            }
            return false;
        }
        
        public bool UnInstallService(string svcName)
        {
            int scParameter = 0x40000000;
            IntPtr sCHANDLE = OpenSCManager(null, null, scParameter);
            if (sCHANDLE.ToInt32() != 0)
            {
                int dwNumServiceArgs = 0x10000;
                IntPtr sVHANDLE = OpenService(sCHANDLE, svcName, dwNumServiceArgs);
                if (sVHANDLE.ToInt32() == 0)
                {
                    return false;
                }
                if (DeleteService(sVHANDLE) != 0)
                {
                    CloseServiceHandle(sCHANDLE);
                    return true;
                }
                CloseServiceHandle(sCHANDLE);
            }
            return false;
        }
    }
}

 