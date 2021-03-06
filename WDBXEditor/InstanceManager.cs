﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using WDBXEditor.Common;

namespace WDBXEditor
{
    public static class InstanceManager
    {
        public static ConcurrentQueue<string> AutoRun = new ConcurrentQueue<string>();
        public static Action AutoRunAdded;

        private static Mutex mutex;
        private static NamedPipeManager pipeServer;

        /// <summary>
        /// Checks a mutex to see if an instance is running and decides how to proceed based on this and args
        /// </summary>
        /// <param name="args"></param>
        public static void InstanceCheck(string[] args)
        {
            Func<string[], bool> ArgCheck = a => a != null && a.Length > 0 && File.Exists(a[0]);
            bool isOnlyInstance = false;

            if (ArgCheck(args) || args.Length == 0)
            {
                mutex = new Mutex(true, "WDBXEditorMutex", out isOnlyInstance);
                if (!isOnlyInstance)
                {
                    Program.PrimaryInstance = false;
                    SendData(args); //Send args to the primary instance
                }
                else
                {
                    Program.PrimaryInstance = true;
                    pipeServer = new NamedPipeManager();
                    pipeServer.ReceiveString += OpenRequest;
                    pipeServer.StartServer();
                }
            }
        }

        /// <summary>
        /// Enqueues recieved file names and launches the AutoRun delegate
        /// </summary>
        /// <param name="filenames"></param>
        public static void OpenRequest(string filenames)
        {
            string[] files = filenames.Split((Char)3);
            Parallel.For(0, files.Length, f =>
            {
                if (Regex.IsMatch(files[f], Constants.FileRegexPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase))
                    AutoRun.Enqueue(files[f]);
            });

            AutoRunAdded?.Invoke();
        }

        public static void Start()
        {
            pipeServer?.StartServer();
        }

        public static void Stop()
        {
            pipeServer?.StopServer();
        }

        /// <summary>
        /// Opens a new version of the application which bypasses the mutex
        /// </summary>
        /// <param name="files"></param>
        /// <returns></returns>
        public static bool LoadNewInstance(IEnumerable<string> files)
        {
            Stop(); //Stop server

            using (Process p = new Process())
            {
                p.StartInfo.FileName = Application.ExecutablePath;
                p.StartInfo.Arguments = string.Join(" ", files);
                bool started = p.Start();

                while (started && p.MainWindowHandle == IntPtr.Zero) //Await the program to fully load
                    Thread.Sleep(50);

                if (Program.PrimaryInstance)
                    Start(); //Start server

                return started;
            }
        }

        public static IEnumerable<string> GetFilesToOpen()
        {
            HashSet<string> files = new HashSet<string>();
            while (AutoRun.Count > 0)
            {
                string file;
                if (AutoRun.TryDequeue(out file) && File.Exists(file))
                    files.Add(file);
            }
            return files;
        }


        #region Send Data
        private static void SendData(string args)
        {
            NamedPipeManager clientPipe = new NamedPipeManager();
            if (clientPipe.Write(args))
                Environment.Exit(0);
        }

        private static void SendData(string[] args)
        {
            SendData(string.Join(((Char)3).ToString(), args));
        }
        #endregion

        #region Flash Methods
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

        [StructLayout(LayoutKind.Sequential)]
        internal struct FLASHWINFO
        {
            public uint cbSize;
            public IntPtr hwnd;
            public uint dwFlags;
            public uint uCount;
            public uint dwTimeout;
        }

        public static bool FlashWindow(Form form)
        {
            FLASHWINFO fInfo = new FLASHWINFO();

            uint FLASHW_ALL = 3;
            uint FLASHW_TIMERNOFG = 12;

            fInfo.cbSize = Convert.ToUInt32(Marshal.SizeOf(fInfo));
            fInfo.hwnd = form.Handle;
            fInfo.dwFlags = FLASHW_ALL | FLASHW_TIMERNOFG;
            fInfo.uCount = uint.MaxValue;
            fInfo.dwTimeout = 0;

            return FlashWindowEx(ref fInfo);
        }
        #endregion

    }
}
