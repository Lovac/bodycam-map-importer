// Folder picker for choosing the Bodycam folder by hand.
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BodycamMapInstaller.Ui
{
    static class FolderPicker
    {
        [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        class FileOpenDialogCom { }

        [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IFileDialog
        {
            [PreserveSig] int Show(IntPtr parent);
            void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);
            void SetFileTypeIndex(uint iFileType);
            void GetFileTypeIndex(out uint piFileType);
            void Advise(IntPtr pfde, out uint pdwCookie);
            void Unadvise(uint dwCookie);
            void SetOptions(uint fos);
            void GetOptions(out uint pfos);
            void SetDefaultFolder(IShellItem psi);
            void SetFolder(IShellItem psi);
            void GetFolder(out IShellItem ppsi);
            void GetCurrentSelection(out IShellItem ppsi);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
            void GetResult(out IShellItem ppsi);
            void AddPlace(IShellItem psi, int fdap);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
            void Close(int hr);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr pFilter);
        }

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItem
        {
            void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem ppsi);
            void GetDisplayName(uint sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
            void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
            void Compare(IShellItem psi, uint hint, out int piOrder);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        static extern void SHCreateItemFromParsingName([MarshalAs(UnmanagedType.LPWStr)] string pszPath, IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IShellItem ppv);

        const uint FOS_PICKFOLDERS = 0x20, FOS_FORCEFILESYSTEM = 0x40, FOS_PATHMUSTEXIST = 0x800;
        const uint SIGDN_FILESYSPATH = 0x80058000;
        const int ERROR_CANCELLED_HR = unchecked((int)0x800704C7);

        public static string Pick(IWin32Window owner, string title, string startFolder)
        {
            try
            {
                IFileDialog d = (IFileDialog)new FileOpenDialogCom();
                try
                {
                    uint opts;
                    d.GetOptions(out opts);
                    d.SetOptions(opts | FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST);
                    d.SetTitle(title);
                    if (!string.IsNullOrEmpty(startFolder) && Directory.Exists(startFolder))
                    {
                        try
                        {
                            IShellItem start;
                            SHCreateItemFromParsingName(startFolder, IntPtr.Zero, typeof(IShellItem).GUID, out start);
                            d.SetFolder(start);
                        }
                        catch (Exception) { }
                    }
                    int hr = d.Show(owner != null ? owner.Handle : IntPtr.Zero);
                    if (hr == ERROR_CANCELLED_HR) return null;
                    if (hr != 0) Marshal.ThrowExceptionForHR(hr);
                    IShellItem item;
                    d.GetResult(out item);
                    string path;
                    item.GetDisplayName(SIGDN_FILESYSPATH, out path);
                    return path;
                }
                finally { Marshal.ReleaseComObject(d); }
            }
            catch (Exception)
            {
                using (FolderBrowserDialog f = new FolderBrowserDialog())
                {
                    f.Description = title;
                    f.ShowNewFolderButton = false;
                    if (!string.IsNullOrEmpty(startFolder) && Directory.Exists(startFolder)) f.SelectedPath = startFolder;
                    return f.ShowDialog(owner) == DialogResult.OK ? f.SelectedPath : null;
                }
            }
        }
    }
}
