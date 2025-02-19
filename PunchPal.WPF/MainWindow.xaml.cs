﻿using Microsoft.Win32;
using PicaPico;
using PunchPal.Core.Events;
using PunchPal.Core.Models;
using PunchPal.Core.Services;
using PunchPal.Core.ViewModels;
using PunchPal.Tools;
using PunchPal.WPF.Controls;
using PunchPal.WPF.Tools;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Shell;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Interop;
using ControlAppearance = Wpf.Ui.Controls.ControlAppearance;
using MainModel = PunchPal.WPF.ViewModels.MainModel;

namespace PunchPal.WPF
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : FluentWindow
    {
        private readonly MainModel _mainModel;
        public MainWindow()
        {
            InitializeComponent();
            _mainModel = new MainModel();
            DataContext = _mainModel;
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
            _mainModel.ConfirmDialog += OnConfirmDialog;
            _mainModel.Tips += OnTips;
            _mainModel.AddRecord += OnAddRecord;
            _mainModel.WorkingHours.TextCoping += OnTextCoping;
            _mainModel.Setting.Personalize.PropertyChanged += OnPersonalizeChanged;
            _mainModel.Setting.Personalize.FileSelecting += OnFileSelecting;
            _mainModel.Setting.WorkingTimeRange.Edited += OnWorkingTimeRangeEdited;
            _mainModel.ShowWindow += OnShowWindow;
            _mainModel.Setting.Common.PropertyChanged += Common_PropertyChanged;
            InitWindowBackdropType();
        }

        private void Common_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(SettingsCommon.ShortcutText))
            {
                return;
            }
            HotKeyTools.Unregister();
            if (_mainModel.Setting.Common.ShortcutModifierKeys != ModifierKeys.None &&
                _mainModel.Setting.Common.ShortcutKey != Key.None)
            {
                var ok = HotKeyTools.Register((System.Windows.Input.ModifierKeys)_mainModel.Setting.Common.ShortcutModifierKeys,
                    (System.Windows.Input.Key)_mainModel.Setting.Common.ShortcutKey);
                if (!ok)
                {
                    _mainModel.Setting.Common.OnShortcutChanged(ModifierKeys.None, Key.None);
                    ShowTips(new TipsOption("提示", "当前快捷键不可用，请重试", Core.Models.ControlAppearance.Caution));
                }
            }
        }

        private async void OnWorkingTimeRangeEdited(object sender, WorkingTimeRange e)
        {
            var content = new EditWorkingTimeRangeControl()
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            if (e != null)
            {
                content.Date = e.DateTime;
                content.IsAllDate = e.Date == 0;
                content.SelectedType = e.Type;
                content.StartTime = new DateTime().AddHours(e.StartHour).AddMinutes(e.StartMinute);
                content.EndTime = new DateTime().AddHours(e.EndHour).AddMinutes(e.EndMinute);
            }
            var contentDialog = new ContentDialog()
            {
                Title = $"{(e == null ? "添加" : "编辑")}工作时间",
                CloseButtonText = "取消",
                PrimaryButtonText = "删除",
                PrimaryButtonAppearance = e != null ? ControlAppearance.Danger : ControlAppearance.Primary,
                IsPrimaryButtonEnabled = e != null && (e.Type != WorkingTimeRangeType.Work || e.Date != 0),
                SecondaryButtonText = "确认",
                SecondaryButtonAppearance = ControlAppearance.Success,
                MinHeight = 0,
                DialogHost = DialogPresenter,
                Content = content
            };
            var result = await contentDialog.ShowAsync();
            switch (result)
            {
                case ContentDialogResult.Primary:
                    {
                        _mainModel.Loading = true;
                        await WorkingTimeRangeService.Instance.Remove(e);
                        _mainModel.Loading = false;
                        ShowTips(new TipsOption("提示", $"删除成功", Core.Models.ControlAppearance.Success));
                        break;
                    }
                case ContentDialogResult.Secondary:
                    {
                        var record = new WorkingTimeRange
                        {
                            Date = content.IsAllDate ? 0 : content.Date.Date.TimestampUnix(),
                            Type = content.SelectedType,
                            StartHour = content.StartTime.Hour,
                            StartMinute = content.StartTime.Minute,
                            EndHour = content.EndTime.Hour,
                            EndMinute = content.EndTime.Minute,
                            UserId = _mainModel.Setting.Common.CurrentUser?.UserId
                        };
                        _mainModel.Loading = true;
                        await WorkingTimeRangeService.Instance.Add(record);
                        _mainModel.Loading = false;
                        ShowTips(new TipsOption("提示", $"{(e == null ? "添加" : "编辑")}成功", Core.Models.ControlAppearance.Success));
                        break;
                    }
                default:
                    return;
            }
            _mainModel.Loading = true;
            await _mainModel.Setting.WorkingTimeRange.InitRanges();
            _mainModel.Loading = false;
        }

        private void OnFileSelecting(object sender, SelectFileEventArgs e)
        {
            var openFileDialog = new OpenFileDialog()
            {
                Filter = e.Filter,
                Multiselect = e.Multiselect
            };
            if (openFileDialog.ShowDialog() != true)
            {
                return;
            }
            e.FileName = openFileDialog.FileName;
            e.FileNames = openFileDialog.FileNames;
        }

        private void OnShowWindow(object sender, EventArgs e)
        {
            Dispatcher.Invoke(ShowWindow);
        }

        private void OnPersonalizeChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(SettingsPersonalize.WindowEffectType))
            {
                return;
            }
            Restart();
        }

        private void OnTextCoping(object sender, string text)
        {
            try
            {
                Clipboard.SetText(text);
                ShowTips(new TipsOption("提示", $"已复制到剪贴板", Core.Models.ControlAppearance.Success));
            }
            catch (Exception)
            {
                ShowTips(new TipsOption("提示", $"复制到剪贴板出错", Core.Models.ControlAppearance.Danger));
            }
        }

        private void OnAddRecord(object sender, EventArgs e)
        {
            if (_mainModel.IsPunchRecord)
            {
                AddPunchRecord();
            }
            else if (_mainModel.IsAttendanceRecord)
            {
                AddAttendanceRecord();
            }
        }

        private async void AddAttendanceRecord()
        {
            var content = new AddAttendanceRecordControl()
            {
                Width = 380,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var contentDialog = new ContentDialog()
            {
                Title = "添加考勤记录",
                CloseButtonText = "取消",
                PrimaryButtonText = "确认",
                MinHeight = 0,
                DialogHost = DialogPresenter,
                Content = content
            };
            var result = await contentDialog.ShowAsync();
            var records = new List<AttendanceRecord>();
            switch (result)
            {
                case ContentDialogResult.Primary:
                    {
                        var startTime = content.StartDateTime.TimestampUnix();
                        var endTime = content.EndDateTime.TimestampUnix();
                        var record = new AttendanceRecord
                        {
                            AttendanceId = DateTime.Now.ToString("yyyyMMddHHmmssfff"),
                            AttendanceTypeId = content.AttendanceTypeId,
                            UserId = _mainModel.Setting.Common.CurrentUser?.UserId,
                            StartTime = startTime,
                            EndTime = endTime,
                            AttendanceTime = DateTime.Now.TimestampUnix(),
                            Remark = content.RecordRemark
                        };
                        records.Add(record);
                        break;
                    }
                default:
                    return;
            }
            _mainModel.Loading = true;
            var len = await AttendanceRecordService.Instance.Add(records);
            _mainModel.Loading = false;
            if (len > 0)
            {
                _mainModel.InitItems();
                ShowTips(new TipsOption("提示", $"添加{len}条数据成功", Core.Models.ControlAppearance.Success));
            }
        }

        private async void AddPunchRecord()
        {
            var content = new AddPunchRecordControl()
            {
                Width = 380,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var contentDialog = new ContentDialog()
            {
                Title = "添加打卡记录",
                CloseButtonText = "取消",
                PrimaryButtonText = "导入",
                PrimaryButtonAppearance = ControlAppearance.Success,
                SecondaryButtonText = "确认",
                SecondaryButtonAppearance = ControlAppearance.Primary,
                MinHeight = 0,
                DialogHost = DialogPresenter,
                Content = content
            };

            var result = await contentDialog.ShowAsync();
            var records = new List<PunchRecord>();
            switch (result)
            {
                case ContentDialogResult.Secondary:
                    {
                        var timestamp = content.RecordDateTime.TimestampUnix();
                        var record = new PunchRecord
                        {
                            PunchTime = timestamp,
                            PunchType = PunchRecord.PunchTypeManual,
                            UserId = _mainModel.Setting.Common.CurrentUser?.UserId,
                            Remark = content.RecordRemark
                        };
                        records.Add(record);
                        break;
                    }
                case ContentDialogResult.Primary:
                    _mainModel.Loading = true;
                    await PunchRecordService.Instance.ImportFromFile(SelectFile(), _mainModel.Setting.Common.CurrentUser?.UserId);
                    _mainModel.Loading = false;
                    return;
                default:
                    break;
            }
            _mainModel.Loading = true;
            var len = await PunchRecordService.Instance.Add(records);
            _mainModel.Loading = false;
            if (len > 0)
            {
                _mainModel.InitItems();
                ShowTips(new TipsOption("提示", $"添加{len}条数据成功", Core.Models.ControlAppearance.Success));
            }
        }

        private string SelectFile()
        {
            var openFileDialog = new OpenFileDialog()
            {
                Filter = "Text|*.txt|Sqlite|*.sqlite|All|*.*",
                Multiselect = false
            };

            if (openFileDialog.ShowDialog() != true) return string.Empty;
            return openFileDialog.FileName;
        }

        private void OnTips(object sender, TipsOption option)
        {
            ShowTips(option);
        }

        private SnackbarService _snackbarService;

        private void ShowTips(TipsOption option)
        {
            if (SnackbarPresenter == null)
            {
                return;
            }

            if (_snackbarService == null)
            {
                _snackbarService = new SnackbarService();
                _snackbarService.SetSnackbarPresenter(SnackbarPresenter);
            }

            _snackbarService.Show(
                option.Title,
                option.Message,
                (ControlAppearance)option.Appearance,
                new SymbolIcon(SymbolRegular.Fluent24),
                option.Duration
            );
        }

        private void OnConfirmDialog(object sender, ConfirmDialogEventArgs e)
        {
            var contentDialog = new ContentDialog()
            {
                Title = e.Title,
                Content = e.Message,
                CloseButtonText = "取消",
                PrimaryButtonText = "确认",
                PrimaryButtonAppearance = (ControlAppearance)e.Appearance,
                MinHeight = 0,
                DialogHost = DialogPresenter,
            };
            var resultTask = contentDialog.ShowAsync();
            e.Result = resultTask.ContinueWith(t =>
            {
                return t.Result == ContentDialogResult.Primary;
            });
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            PunchNavigationView.Navigate("记录");
            ShowWindow();
            LockScreenTools.Register(new WindowInteropHelper(this).Handle, OnLockScreen);
            HotKeyTools.SetCallback(this, OnHotKey);
            Common_PropertyChanged(null, new PropertyChangedEventArgs(nameof(SettingsCommon.ShortcutText)));
        }

        private void ToWorkTimeEdit(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _mainModel.Setting.CurrentSettingPage = SettingsModel.PageType.WorkingTimeRange;
            PunchNavigationView.Navigate("设置");
        }

        private void OnHotKey()
        {
            if (IsVisible)
            {
                Close();
            }
            else
            {
                ShowWindow();
            }
        }

        private bool _todayNotifyStartPunch;
        private async void OnLockScreen(bool locked)
        {
            var settings = SettingsModel.Load();

            if (settings.Data.IsAutoAddRecordAtLock)
            {
                await PunchRecordService.Instance.Add(new PunchRecord()
                {
                    PunchTime = DateTime.Now.TimestampUnix(),
                    UserId = settings.Common.CurrentUser?.UserId,
                    PunchType = locked ? PunchRecord.PunchTypeLock : PunchRecord.PunchTypeUnLock
                });
                _mainModel.InitItems();
            }

            if (!locked && settings.Common.IsNotifyStartPunch && !_todayNotifyStartPunch)
            {
                _todayNotifyStartPunch = true;
                NotifyStartPunch();
                return;
            }

            var workTime = await WorkingTimeRangeService.Instance.CurrentItems();
            if (!locked || !settings.Common.IsNotifyLockPunch || workTime == null || workTime.Work == null)
            {
                return;
            }

            var now = DateTime.Now;
            var offDuty = new DateTime(now.Year, now.Month, now.Day, workTime.Work.EndHour, workTime.Work.EndMinute,
                0);
            if (now < offDuty)
            {
                return;
            }

            NotifyEndPunch();
        }

        private async void NotifyEndPunch()
        {
            var record = PunchRecordService.Instance.TodayFirst(SettingsModel.Load().Data.DayStartHour);
            if (record != null)
            {
                ShowToast("记得下班打卡哦！！！", true);
            }
        }

        private async void NotifyStartPunch()
        {
            var record = await PunchRecordService.Instance.TodayFirst(SettingsModel.Load().Data.DayStartHour);
            if (record == null)
            {
                ShowToast("记得上班打卡哦！！！", true);
            }
        }

        private void InitWindowBackdropType()
        {
            if (!OSVersionTools.IsAcrylicCustom || _mainModel.WindowBackdropType != WindowBackdropType.Acrylic) return;
            AcrylicHelper.Apply(this, DragHelper);
        }

        private void PunchNavigationView_Navigating(NavigationView sender, NavigatingCancelEventArgs args)
        {
            if (!(args.Page is Page page))
            {
                return;
            }
            switch (page.GetType())
            {
                case var type when type == typeof(Pages.SettingCommonPage):
                    if (page.DataContext == null) page.DataContext = _mainModel.Setting.Common;
                    _mainModel.Setting.CurrentSettingPage = SettingsModel.PageType.Common;
                    break;
                case var type when type == typeof(Pages.SettingDataPage):
                    if (page.DataContext == null) page.DataContext = _mainModel.Setting.Data;
                    _mainModel.Setting.CurrentSettingPage = SettingsModel.PageType.Data;
                    break;
                case var type when type == typeof(Pages.SettingCalendarPage):
                    if (page.DataContext == null) page.DataContext = _mainModel.Setting.Calendar;
                    _mainModel.Setting.CurrentSettingPage = SettingsModel.PageType.Calendar;
                    break;
                case var type when type == typeof(Pages.SettingPersonalizePage):
                    if (page.DataContext == null) page.DataContext = _mainModel.Setting.Personalize;
                    _mainModel.Setting.CurrentSettingPage = SettingsModel.PageType.Personalize;
                    break;
                case var type when type == typeof(Pages.SettingWorkingTimeRangePage):
                    if (page.DataContext == null) page.DataContext = _mainModel.Setting.WorkingTimeRange;
                    _mainModel.Setting.CurrentSettingPage = SettingsModel.PageType.WorkingTimeRange;
                    break;
                case var type when type == typeof(Pages.SettingDataSourcePage):
                    if (page.DataContext == null) page.DataContext = _mainModel.Setting.DataSource;
                    _mainModel.Setting.CurrentSettingPage = SettingsModel.PageType.DataSource;
                    break;
                case var type when type == typeof(Pages.SettingNetworkPage):
                    if (page.DataContext == null) page.DataContext = _mainModel.Setting.Network;
                    _mainModel.Setting.CurrentSettingPage = SettingsModel.PageType.Network;
                    break;
                case var type when type == typeof(Pages.SettingAboutPage):
                    if (page.DataContext == null) page.DataContext = _mainModel.Setting.About;
                    _mainModel.Setting.CurrentSettingPage = SettingsModel.PageType.Abount;
                    break;
                case var type when type == typeof(Pages.PunchRecordPage):
                    _mainModel.CurrentPage = Core.ViewModels.MainModel.PageType.PunchRecord;
                    if (page.DataContext == null) page.DataContext = _mainModel.PunchRecord;
                    break;
                case var type when type == typeof(Pages.AttendanceRecordPage):
                    _mainModel.CurrentPage = Core.ViewModels.MainModel.PageType.AttendanceRecord;
                    if (page.DataContext == null) page.DataContext = _mainModel.AttendanceRecord;
                    break;
                case var type when type == typeof(Pages.WorkingHoursPage):
                    _mainModel.CurrentPage = Core.ViewModels.MainModel.PageType.WorkingHours;
                    if (page.DataContext == null) page.DataContext = _mainModel.WorkingHours;
                    break;
                case var type when type == typeof(Pages.CalendarPage):
                    _mainModel.CurrentPage = Core.ViewModels.MainModel.PageType.Calendar;
                    if (page.DataContext == null) page.DataContext = _mainModel.Calendar;
                    break;
                case var type when type == typeof(Pages.OverviewPage):
                    _mainModel.CurrentPage = Core.ViewModels.MainModel.PageType.Overview;
                    if (page.DataContext == null) page.DataContext = _mainModel.Overview;
                    break;
                case var type when type == typeof(Pages.SettingsPage):
                    _mainModel.CurrentPage = Core.ViewModels.MainModel.PageType.Settings;
                    if (page.DataContext == null) page.DataContext = _mainModel.Setting;
                    break;
                default:
                    _mainModel.CurrentPage = Core.ViewModels.MainModel.PageType.None;
                    break;
            }
        }

        private bool _exiting;

        private void Exit(object sender, RoutedEventArgs e)
        {
            _exiting = true;
            _ = _mainModel.Setting.SaveReal();
            ExitToast();
            Application.Current.Shutdown();
        }

        private async void Restart()
        {
            var option = new ConfirmDialogEventArgs()
            {
                Title = "提示",
                Message = "设置已更改，是否重启应用？",
                Appearance = Core.Models.ControlAppearance.Caution
            };
            _ = _mainModel.Setting.SaveReal();
            OnConfirmDialog(null, option);
            if (option.Result == null)
            {
                return;
            }
            var result = await option.Result;
            if (!result)
            {
                return;
            }
            _exiting = true;
            Process.Start(Application.ResourceAssembly.Location, "--restarted");
            Application.Current.Shutdown();
        }

        private bool _toastShown;

        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            e.Cancel = true;
            Hide();
            if (_toastShown)
            {
                return;
            }

            ShowToast("已最小化到托盘");
            _toastShown = true;
        }

        private void ShowWindow()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
            var handle = new WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero)
            {
                SetForegroundWindow(handle);
            }
        }

        private void OnShowWindowClick(object sender, RoutedEventArgs e)
        {
            ShowWindow();
        }

        private void OnShowSettingsClick(object sender, RoutedEventArgs e)
        {
            ShowWindow();
            PunchNavigationView.Navigate("设置");
        }

        protected override void OnExtendsContentIntoTitleBarChanged(bool oldValue, bool newValue)
        {
            SetCurrentValue(WindowStyleProperty, WindowStyle);

            WindowChrome.SetWindowChrome(
                this,
                new WindowChrome
                {
                    CaptionHeight = 0,
                    CornerRadius = default,
                    GlassFrameThickness = new Thickness(-1),
                    ResizeBorderThickness = ResizeMode == ResizeMode.NoResize ? default : new Thickness(4),
                    UseAeroCaptionButtons = false,
                }
            );
            _ = UnsafeNativeMethods.RemoveWindowTitlebarContents(this);
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        private void OnWindowMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            FocusHelper.Focus();
        }

#if NETFRAMEWORK
        private void OnToastActivated(Microsoft.Toolkit.Uwp.Notifications.ToastNotificationActivatedEventArgsCompat e)
        {
            ShowWindow();
        }

        public static void ShowToast(string message, bool longDuration = false)
        {
            try
            {
                Microsoft.Toolkit.Uwp.Notifications.ToastNotificationManagerCompat.History.Clear();
                var builder = new Microsoft.Toolkit.Uwp.Notifications.ToastContentBuilder()
                    .AddArgument(NameTools.AppName)
                    .AddText(message);
                builder.SetToastDuration(longDuration
                    ? Microsoft.Toolkit.Uwp.Notifications.ToastDuration.Long
                    : Microsoft.Toolkit.Uwp.Notifications.ToastDuration.Short);
                builder.Show();
            }
            catch (Exception)
            {
                // ignore
            }
        }

        public static void ExitToast()
        {
            Microsoft.Toolkit.Uwp.Notifications.ToastNotificationManagerCompat.History.Clear();
            Microsoft.Toolkit.Uwp.Notifications.ToastNotificationManagerCompat.Uninstall();
        }
#else
        public static void ShowToast(string message, bool longDuration = false) { }
        public static void ExitToast() { }
#endif
    }
}