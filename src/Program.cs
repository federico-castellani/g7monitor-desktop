using System;
using System.Threading;
using System.Windows.Forms;
using G7MonitorDesktop.Localization;
using G7MonitorDesktop.Models;
using G7MonitorDesktop.Services;
using G7MonitorDesktop.UI;

namespace G7MonitorDesktop
{
    public class G7MonitorDesktopContext : ApplicationContext
    {
        private AppConfig config;
        private G7MonitorSyncService syncService;
        private TaskbarWidget taskbarWidget;
        private FlyoutWindow flyout;
        private ContextMenuStrip contextMenu;
        private System.Windows.Forms.Timer ageTimer;
        private bool exiting;

        public G7MonitorDesktopContext()
        {
            AppConfig.MigrateFromLegacyName();
            config = AppConfig.Load();
            Strings.Apply(config.Language);

            // 1. Windows 11 Popup Flyout Window
            flyout = new FlyoutWindow(config);
            flyout.ConfigSaved += (newCfg) =>
            {
                this.config = newCfg;
                syncService.UpdateConfig(newCfg);
                taskbarWidget.UpdateConfig(newCfg);
            };
            flyout.ExitRequested += ExitApp;

            // 2. Taskbar Quick-Glance Pill Widget
            taskbarWidget = new TaskbarWidget(config);
            taskbarWidget.SetFlyoutHandle(flyout.Handle);
            taskbarWidget.Clicked += ToggleFlyout;
            taskbarWidget.RightClicked += ShowContextMenu;
            taskbarWidget.Show();

            contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("", null, (s, e) => ToggleFlyout());
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add("", null, (s, e) => ExitApp());

            // 3. G7 Monitor Server sync (events arrive on a background thread: both views marshal to the UI thread)
            syncService = new G7MonitorSyncService(config);
            syncService.OnReadingsUpdated += (readings, latest, delta) =>
            {
                if (exiting) return;
                try
                {
                    taskbarWidget.UpdateGlucose(latest, delta);
                    flyout.UpdateReadings(readings, latest, delta);
                }
                catch (InvalidOperationException) { } // windows closed while exiting
            };
            syncService.OnStatusChanged += (state, retrySeconds) =>
            {
                if (exiting) return;
                try
                {
                    taskbarWidget.UpdateStatus(state, retrySeconds);
                    flyout.UpdateStatus(state, retrySeconds);
                }
                catch (InvalidOperationException) { }
            };
            syncService.Start();

            // 4. Reading age / staleness changes with time even without new data.
            ageTimer = new System.Windows.Forms.Timer { Interval = 30000 };
            ageTimer.Tick += (s, e) =>
            {
                taskbarWidget.Render();
                if (flyout.Visible) flyout.Invalidate();
            };
            ageTimer.Start();
        }

        private void ToggleFlyout()
        {
            if (flyout.IsRecentlyClosed) return;

            if (flyout.Visible && !flyout.IsClosing)
            {
                flyout.HideAnimated();
            }
            else
            {
                flyout.ShowNear(taskbarWidget.Bounds);
            }
        }

        private void ShowContextMenu()
        {
            // Texts set on every opening: they follow a language change without rebuilding the menu.
            contextMenu.Items[0].Text = Strings.Current.MenuOpen;
            contextMenu.Items[2].Text = Strings.Current.MenuExit;
            contextMenu.Show(Cursor.Position);
        }

        private void ExitApp()
        {
            if (exiting) return;
            exiting = true;

            if (ageTimer != null) { ageTimer.Stop(); ageTimer.Dispose(); }
            if (syncService != null) syncService.Stop();
            if (contextMenu != null) contextMenu.Dispose();
            if (taskbarWidget != null)
            {
                taskbarWidget.Close();
                taskbarWidget.Dispose();
            }
            if (flyout != null)
            {
                flyout.Close();
                flyout.Dispose();
            }
            ExitThread();
        }
    }

    internal static class Program
    {
        private static Mutex appMutex;

        [STAThread]
        private static void Main()
        {
            try
            {
                bool createdNew;
                appMutex = new Mutex(true, "G7MonitorDesktop_App_Mutex", out createdNew);
                if (!createdNew)
                {
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new G7MonitorDesktopContext());
            }
            catch (Exception ex)
            {
                MessageBox.Show(Strings.Current.Format(Strings.Current.FatalError, ex.Message), "G7 Monitor Desktop", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (appMutex != null) appMutex.Dispose();
            }
        }
    }
}
