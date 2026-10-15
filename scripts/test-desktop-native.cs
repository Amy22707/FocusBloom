using System;
using System.Runtime.InteropServices;
using System.Text;
public static class FocusBloomTestDesktop
{
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] public struct Startup { public int cb; public string reserved, desktop, title; public int x,y,width,height,xChars,yChars,fill,flags; public short show,reservedSize; public IntPtr reservedData,input,output,error; }
    [StructLayout(LayoutKind.Sequential)] public struct ProcessInfo { public IntPtr process, thread; public int pid, tid; }
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] public static extern IntPtr CreateDesktop(string name,IntPtr device,IntPtr mode,int flags,int access,IntPtr security);
    [DllImport("user32.dll",SetLastError=true)] public static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll",SetLastError=true)] static extern IntPtr OpenInputDesktop(int flags,bool inherit,int access);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool GetUserObjectInformation(IntPtr handle,int index,StringBuilder data,int length,out int needed);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] public static extern bool CreateProcess(string application,StringBuilder command,IntPtr processSecurity,IntPtr threadSecurity,bool inherit,int flags,IntPtr environment,string directory,ref Startup startup,out ProcessInfo process);
    [DllImport("kernel32.dll")] public static extern int WaitForSingleObject(IntPtr handle,int milliseconds);
    [DllImport("kernel32.dll")] public static extern bool GetExitCodeProcess(IntPtr process,out int code);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr handle);
    public static string InputName() { var handle=OpenInputDesktop(0,false,1); if(handle==IntPtr.Zero)throw new System.ComponentModel.Win32Exception();try {var text=new StringBuilder(256);int needed;if(!GetUserObjectInformation(handle,2,text,512,out needed))throw new System.ComponentModel.Win32Exception();return text.ToString();} finally{CloseDesktop(handle);} }
}
