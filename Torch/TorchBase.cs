﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using Sandbox;
using Sandbox.ModAPI;
using Torch.API;
using Torch.Managers;
using VRage.Scripting;

namespace Torch
{
    public abstract class TorchBase : ITorchBase
    {
        /// <summary>
        /// Dirty hack because *keen*
        /// Use only if absolutely necessary.
        /// </summary>
        [Obsolete]
        public static ITorchBase Instance { get; private set; }
        protected static Logger Log = LogManager.GetLogger("Torch");
        public Version Version { get; protected set; }
        public string[] RunArgs { get; set; }
        public IPluginManager Plugins { get; protected set; }
        public IMultiplayer Multiplayer { get; protected set; }
        public event Action SessionLoaded;

        private bool _init;

        protected void InvokeSessionLoaded()
        {
            SessionLoaded?.Invoke();
        }

        protected TorchBase()
        {
            if (Instance != null)
                throw new InvalidOperationException("A TorchBase instance already exists.");

            Instance = this;

            Version = Assembly.GetExecutingAssembly().GetName().Version; 
            RunArgs = new string[0];
            Plugins = new PluginManager(this);
            Multiplayer = new MultiplayerManager(this);
        }

        /// <summary>
        /// Invokes an action on the game thread.
        /// </summary>
        /// <param name="action"></param>
        public void Invoke(Action action)
        {
            MySandboxGame.Static.Invoke(action);
        }

        /// <summary>
        /// Invokes an action on the game thread asynchronously.
        /// </summary>
        /// <param name="action"></param>
        public Task InvokeAsync(Action action)
        {
            if (Thread.CurrentThread == MySandboxGame.Static.UpdateThread)
            {
                Debug.Assert(false, $"{nameof(InvokeAsync)} should not be called on the game thread.");
                action?.Invoke();
                return Task.CompletedTask;
            }

            return Task.Run(() => InvokeBlocking(action));
        }

        /// <summary>
        /// Invokes an action on the game thread and blocks until it is completed.
        /// </summary>
        /// <param name="action"></param>
        public void InvokeBlocking(Action action)
        {
            if (action == null)
                return;

            if (Thread.CurrentThread == MySandboxGame.Static.UpdateThread)
            {
                Debug.Assert(false, $"{nameof(InvokeBlocking)} should not be called on the game thread.");
                action.Invoke();
                return;
            }

            var e = new AutoResetEvent(false);

            MySandboxGame.Static.Invoke(() =>
            {
                try
                {
                    action.Invoke();
                }
                catch (Exception ex)
                {
                    //log
                }
                finally
                {
                    e.Set();
                }
            });

            if (!e.WaitOne(60000))
                throw new TimeoutException("The game action timed out.");
        }

        public virtual void Init()
        {
            Debug.Assert(!_init, "Torch instance is already initialized.");

            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            _init = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Log.Fatal((Exception)e.ExceptionObject);
        }

        public abstract void Start();
        public abstract void Stop();
    }
}