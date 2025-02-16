﻿using log4net;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TextLocator.Core;
using TextLocator.Enums;
using TextLocator.HotKey;
using TextLocator.Index;
using TextLocator.Message;
using TextLocator.Util;

namespace TextLocator
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>
        /// 全部
        /// </summary>
        private RadioButton _radioButtonAll;
        /// <summary>
        /// 时间戳
        /// </summary>
        private long _timestamp;
        /// <summary>
        /// 搜索参数
        /// </summary>
        private Entity.SearchParam _searchParam;
        /// <summary>
        /// 上次预览区搜索文本
        /// </summary>
        private string _lastPreviewSearchText;
        /// <summary>
        /// 索引构建中
        /// </summary>
        private static volatile bool build = false;

        /// <summary>
        /// 窗口状态
        /// </summary>
        private WindowState _windowState = WindowState.Normal;

        #region 热键
        /// <summary>
        /// 当前窗口句柄
        /// </summary>
        private IntPtr _hwnd = new IntPtr();
        /// <summary>
        /// 记录快捷键注册项的唯一标识符
        /// </summary>
        private Dictionary<HotKeySetting, int> _hotKeySettings = new Dictionary<HotKeySetting, int>();
        #endregion

        public MainWindow()
        {
            InitializeComponent();
        }

        #region 窗口初始化
        /// <summary>
        /// WPF窗体的资源初始化完成，并且可以通过WindowInteropHelper获得该窗体的句柄用来与Win32交互后调用
        /// </summary>
        /// <param name="e"></param>
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            // 获取窗体句柄
            _hwnd = new WindowInteropHelper(this).Handle;
            HwndSource hWndSource = HwndSource.FromHwnd(_hwnd);
            // 添加处理程序
            if (hWndSource != null) hWndSource.AddHook(WndProc);
        }

        /// <summary>
        /// 所有控件初始化完成后调用
        /// </summary>
        /// <param name="e"></param>
        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            // 注册热键
            InitHotKey();
        }

        /// <summary>
        /// 加载完毕
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // 初始化应用信息
            InitializeAppInfo();

            // 初始化配置文件信息
            InitializeAppConfig();

            // 初始化文件类型列表
            InitializeSearchFileType();

            // 初始化排序类型列表
            InitializeSortType();

            // 初始化搜索域列表
            InitializeSearchRegion();

            // 清理事件（必须放在初始化之后，否则类型筛选的选中Reset可能存在错误）
            ResetSearchResult();

            // 检查索引是否存在：如果存在才执行更新检查，不存在的跳过更新检查。
            if (CheckIndexExist(false))
            {
                // 软件每次启动时执行索引更新逻辑？
                IndexUpdateTask();
            }

            // 注册全局热键时间
            HotKeySettingManager.Instance.RegisterGlobalHotKeyEvent += Instance_RegisterGlobalHotKeyEvent;
        }

        /// <summary>
        /// 窗口关闭中，改为隐藏
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Window_Closing(object sender, CancelEventArgs e)
        {
            _windowState = this.WindowState;
            this.Hide();
            e.Cancel = true;
        }

        /// <summary>
        /// 窗口激活
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Window_Activated(object sender, EventArgs e)
        {
            this.Show();
            this.WindowState = _windowState;
        }
        #endregion

        #region 程序初始化
        /// <summary>
        /// 初始化应用信息
        /// </summary>
        private void InitializeAppInfo()
        {
            // 获取程序版本
            Version version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
			
			// 设置标题
            this.Title = string.Format("{0} v{1} (开放版)", this.Title, version);

        }

        /// <summary>
        /// 初始化排序类型列表
        /// </summary>
        private void InitializeSortType()
        {
            TaskTime taskTime = TaskTime.StartNew();
            Array sorts = Enum.GetValues(typeof(SortType));
            SortOptions.Items.Clear();
            foreach (var sort in sorts)
            {
                SortOptions.Items.Add(sort);
            }
            log.Debug("InitializeSortType 耗时：" + taskTime.ConsumeTime + "。");
        }

        /// <summary>
        /// 初始化搜索域
        /// </summary>
        private void InitializeSearchRegion()
        {
            TaskTime taskTime = TaskTime.StartNew();
            Array regions = Enum.GetValues(typeof(SearchRegion));
            SearchScope.Items.Clear();
            foreach (var region in regions)
            {
                SearchScope.Items.Add(region);
            }
            log.Debug("InitializeSearchRegion 耗时：" + taskTime.ConsumeTime + "。");
        }

        /// <summary>
        /// 初始化文件类型过滤器列表
        /// </summary>
        private void InitializeSearchFileType()
        {
            TaskTime taskTime = TaskTime.StartNew();
            // 文件类型筛选下拉框数据初始化
            SearchFileType.Children.Clear();
            // 遍历文件类型枚举
            foreach (FileType fileType in Enum.GetValues(typeof(FileType)))
            {
                // 构造UI元素
                RadioButton radioButton = new RadioButton()
                {
                    GroupName = "SearchFileType",
                    Name = "FileType" + fileType.ToString(),
                    Width = 80,
                    Margin = new Thickness(1),
                    Tag = fileType,
                    Content = fileType.ToString(),                    
                    IsChecked = fileType == FileType.全部
                };
                if (fileType != FileType.全部)
                {
                    radioButton.ToolTip = fileType.GetDescription();
                }
                radioButton.Checked += FileType_Checked;
                SearchFileType.Children.Add(radioButton);

                // 缓存全部，用于还原到默认值（因为默认选中全部）
                if (fileType == FileType.全部)
                {
                    _radioButtonAll = radioButton;
                }
            }
            // 搜索筛选条件直接读取的当前值，初始化时默认赋值全部。其他选项修改时会更改此值
            SearchFileType.Tag = FileType.全部;
            log.Debug("InitializeSearchFileTypes 耗时：" + taskTime.ConsumeTime + "。");
        }

        /// <summary>
        /// 初始化配置文件信息
        /// </summary>
        private void InitializeAppConfig()
        {
            TaskTime taskTime = TaskTime.StartNew();

            // 启用的搜索区域信息显示
            List<Entity.AreaInfo> enableAreaInfos = AreaUtil.GetEnableAreaInfoList();
            string enableAreaNames = "";
            string enableAreaNameDescs = "";
            foreach (Entity.AreaInfo areaInfo in enableAreaInfos)
            {
                enableAreaNames += areaInfo.AreaName + "，";
                enableAreaNameDescs += areaInfo.AreaName + "：" + string.Join(",", areaInfo.AreaFolders.ToArray()) + "\r\n";
            }
            this.EnableAreaInfos.Text = enableAreaNames.Substring(0, enableAreaNames.Length - 1);
            this.EnableAreaInfos.ToolTip = enableAreaNameDescs.Substring(0, enableAreaNameDescs.Length - 2);

            // 未启用的搜索区域信息显示
            List<Entity.AreaInfo> disableAreaInfos = AreaUtil.GetDisableAreaInfoList();
            string disableAreaNames = "";
            string disableAreaNameDescs = "";
            foreach (Entity.AreaInfo areaInfo in disableAreaInfos)
            {
                disableAreaNames += areaInfo.AreaName + "，";
                disableAreaNameDescs += areaInfo.AreaName + "：" + string.Join(",", areaInfo.AreaFolders.ToArray()) + "\r\n";
            }
            this.DisableAreaInfos.Text = string.IsNullOrEmpty(disableAreaNames) ? disableAreaNames : disableAreaNames.Substring(0, disableAreaNames.Length - 1);
            if (!string.IsNullOrEmpty(disableAreaNameDescs))
            {
                this.DisableAreaInfos.ToolTip = disableAreaNameDescs.Substring(0, disableAreaNameDescs.Length - 2);
            }            

            // 读取分页每页显示条数
            if (string.IsNullOrEmpty(AppUtil.ReadValue("AppConfig", "ResultListPageSize", "")))
            {
                AppUtil.WriteValue("AppConfig", "ResultListPageSize", AppConst.MRESULT_LIST_PAGE_SIZE + "");
            }

            log.Debug("InitializeAppConfig 耗时：" + taskTime.ConsumeTime + "。");
        }

        #endregion

        #region 热键注册
        /// <summary>
        /// 通知注册系统快捷键事件处理函数
        /// </summary>
        /// <param name="hotKeyModelList"></param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        private bool Instance_RegisterGlobalHotKeyEvent(System.Collections.ObjectModel.ObservableCollection<HotKeyModel> hotKeyModelList)
        {
            InitHotKey(hotKeyModelList);
            return true;
        }

        /// <summary>
        /// 初始化注册快捷键
        /// </summary>
        /// <param name="hotKeyModelList">待注册热键的项</param>
        /// <returns>true:保存快捷键的值；false:弹出设置窗体</returns>
        private async Task<bool> InitHotKey(ObservableCollection<HotKeyModel> hotKeyModelList = null)
        {
            var list = hotKeyModelList ?? HotKeySettingManager.Instance.LoadDefaultHotKey();
            // 注册全局快捷键
            string failList = HotKeyHelper.RegisterGlobalHotKey(list, _hwnd, out _hotKeySettings);
            if (string.IsNullOrEmpty(failList))
                return true;

            var result = await MessageCore.Confirm(string.Format("无法注册下列快捷键：\r\n\r\n{0}是否要改变这些快捷键？", failList), "确认提示", MessageBoxButton.YesNo);
            // 弹出热键设置窗体
            var win = HotkeyWindow.CreateInstance();
            if (result == MessageBoxResult.Yes)
            {
                if (!win.IsVisible)
                {
                    win.Topmost = true;
                    win.ShowDialog();
                }
                else
                {
                    win.Activate();
                }
                return false;
            }
            return true;
        }

        /// <summary>
        /// 窗体回调函数，接收所有窗体消息的事件处理函数
        /// </summary>
        /// <param name="hWnd">窗口句柄</param>
        /// <param name="msg">消息</param>
        /// <param name="wideParam">附加参数1</param>
        /// <param name="longParam">附加参数2</param>
        /// <param name="handled">是否处理</param>
        /// <returns>返回句柄</returns>
        private IntPtr WndProc(IntPtr hWnd, int msg, IntPtr wideParam, IntPtr longParam, ref bool handled)
        {
            var hotKeySetting = new HotKeySetting();
            switch (msg)
            {
                case HotKeyManager.WM_HOTKEY:
                    int sid = wideParam.ToInt32();
                    // 显示
                    if (sid == _hotKeySettings[HotKeySetting.显示])
                    {
                        hotKeySetting = HotKeySetting.显示;

                        this.Show();
                        this.WindowState = WindowState.Normal;
                    }
                    // 隐藏
                    else if (sid == _hotKeySettings[HotKeySetting.隐藏])
                    {
                        hotKeySetting = HotKeySetting.隐藏;
                        this.Hide();
                    }
                    // 清空
                    else if (sid == _hotKeySettings[HotKeySetting.清空])
                    {
                        hotKeySetting = HotKeySetting.清空;
                        ResetSearchResult();
                    }
                    // 退出
                    else if (sid == _hotKeySettings[HotKeySetting.退出])
                    {
                        hotKeySetting = HotKeySetting.退出;
                        AppCore.Shutdown();
                    }
                    // 上一项
                    else if (sid == _hotKeySettings[HotKeySetting.上一个])
                    {
                        hotKeySetting = HotKeySetting.上一个;
                        Switch2Preview(HotKeySetting.上一个);
                    }
                    // 下一项
                    else if (sid == _hotKeySettings[HotKeySetting.下一个])
                    {
                        hotKeySetting = HotKeySetting.下一个;
                        Switch2Preview(HotKeySetting.下一个);
                    }
                    log.Debug(string.Format("触发【{0}】快捷键", hotKeySetting));
                    handled = true;
                    break;
            }
            return IntPtr.Zero;
        }
        #endregion

        #region 搜索
        /// <summary>
        /// 搜索
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            // 获取搜索关键词列表
            List<string> keywords = GetSearchTextKeywords();
            if (keywords.Count <= 0)
            {
                MessageCore.ShowWarning("请输入搜索关键词");
                return;
            }

            // ---- 搜索按钮时，下拉框和其他筛选条件全部恢复默认值
            // 取消精确检索
            PreciseRetrieval.IsChecked = false;
            // 取消匹配全词
            MatchWords.IsChecked = false;

            // 全部文件类型
            ToggleButtonAutomationPeer toggleButtonAutomationPeer = new ToggleButtonAutomationPeer(_radioButtonAll);
            IToggleProvider toggleProvider = toggleButtonAutomationPeer.GetPattern(PatternInterface.Toggle) as IToggleProvider;
            toggleProvider.Toggle();

            // 默认排序
            SortOptions.SelectedIndex = 0;
            // 文件名和内容
            // SearchScope.SelectedIndex = 0;

            BeforeSearch();
        }

        /// <summary>
        /// 回车搜索
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SearchText_PreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                // ---- 光标移除文本框
                SearchText.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));

                // ---- 搜索按钮时，下拉框和其他筛选条件全部恢复默认值
                // 取消精确检索
                PreciseRetrieval.IsChecked = false;
                // 取消匹配全词
                MatchWords.IsChecked = false;

                // 全部文件类型
                ToggleButtonAutomationPeer toggleButtonAutomationPeer = new ToggleButtonAutomationPeer(_radioButtonAll);
                IToggleProvider toggleProvider = toggleButtonAutomationPeer.GetPattern(PatternInterface.Toggle) as IToggleProvider;
                toggleProvider.Toggle();

                // 默认排序
                SortOptions.SelectedIndex = 0;
                // 文件名和内容
                // SearchScope.SelectedIndex = 0;

                BeforeSearch();

                // 光标聚焦
                SearchText.Focus();
            }
        }

        /// <summary>
        /// 文本内容变化时
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SearchText_TextChanged(object sender, TextChangedEventArgs e)
        {
            // 如果文本为空则隐藏清空按钮，如果不为空则显示清空按钮
            this.CleanButton.Visibility = this.SearchText.Text.Length > 0 ? Visibility.Visible : Visibility.Hidden;
            // 文本框颜色
            SearchTextBorder.BorderBrush = new SolidColorBrush(this.SearchText.Text.Length > 0 ? Colors.Green : (Color)ColorConverter.ConvertFromString("#2196f3"));
        }

        /// <summary>
        /// 搜索
        /// </summary>
        /// <param name="timestamp">时间戳，用于校验为同一子任务；时间戳不相同表名父任务结束，子任务跳过执行</param>
        /// <param name="searchParam">搜索条件</param>
        private void Search(long timestamp, Entity.SearchParam searchParam)
        {
            if (!CheckIndexExist())
            {
                return;
            }

            ShowStatus("搜索处理中...");

            Thread t = new Thread(() =>
            {
                try
                {
                    // 1、---- 清空搜索结果列表
                    Dispatcher.Invoke(new Action(() =>
                    {
                        this.SearchResultList.Items.Clear();
                    }));

                    // 2、---- 查询列表（参数，消息回调）
                    Entity.SearchResult searchResult = IndexCore.Search(searchParam, ShowStatus);

                    // 验证列表数据
                    if (null == searchResult || searchResult.Results.Count <= 0)
                    {
                        this.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            MessageCore.ShowWarning("没有搜到你想要的内容，请更换搜索条件。");
                        }));
                        return;
                    }

                    // 3、---- 遍历结果
                    foreach (Entity.FileInfo fileInfo in searchResult.Results)
                    {
                        if (_timestamp != timestamp)
                        {
                            return;
                        }
                        this.Dispatcher.Invoke(new Action(() =>
                        {
                            this.SearchResultList.Items.Add(new FileInfoItem(fileInfo, searchParam.SearchRegion));
                        }));
                    }

                    // 4、---- 显示预览列表分页信息
                    Dispatcher.Invoke(new Action(() =>
                    {
                        this.PreviewPage.Text = string.Format("0/{0}", SearchResultList.Items.Count);
                    }));

                    // 5、---- 分页总数
                    this.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        // 如果总条数小于等于分页条数，则不显示分页
                        this.PageBar.Total = searchResult.Total > PageSize ? searchResult.Total : 0;

                        // 上一个和下一个切换面板是否显示
                        this.SwitchPreview.Visibility = searchResult.Total > 0 ? Visibility.Visible : Visibility.Hidden;
                    }));
                }
                catch (Exception ex)
                {
                    log.Error("搜索错误：" + ex.Message, ex);
                }
            });
            t.Priority = ThreadPriority.Highest;
            t.Start();
        }
        #endregion

        #region 分页
        /// <summary>
        /// 当前页
        /// </summary>
        public int PageNow = 1;
        /// <summary>
        /// 实现INotifyPropertyChanged接口
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;
        public void OnPropertyChanged(string propertyName)
        {
            if (this.PropertyChanged != null)
            {
                this.PropertyChanged.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }
        /// <summary>
        /// 每页显示数量
        /// </summary>
        public int PageSize
        {
            // 获取值时将私有字段传出；
            get { return AppConst.MRESULT_LIST_PAGE_SIZE; }
            set
            {
                // 赋值时将值传给私有字段
                AppConst.MRESULT_LIST_PAGE_SIZE = value;
                // 一旦执行了赋值操作说明其值被修改了，则立马通过INotifyPropertyChanged接口告诉UI(IntValue)被修改了
                OnPropertyChanged("PageSize");
            }
        }

        // 切换页码
        private void PageBar_PageIndexChanged(object sender, RoutedPropertyChangedEventArgs<int> e)
        {
            log.Debug($"pageIndex : {e.OldValue} => {e.NewValue}");

            BeforeSearch(e.NewValue);
        }

        private void PageBar_PageSizeChanged(object sender, RoutedPropertyChangedEventArgs<int> e)
        {
            log.Debug($"pageSize : {e.OldValue} => {e.NewValue}");
        }
        #endregion

        #region 排序
        /// <summary>
        /// 排序选中
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SortOptions_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            BeforeSearch(PageNow);
        }
        #endregion

        #region 清空
        /// <summary>
        /// 清空按钮
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CleanButton_Click(object sender, RoutedEventArgs e)
        {
            ResetSearchResult();
        }

        /// <summary>
        /// 清理查询结果
        /// </summary>
        private void ResetSearchResult()
        {
            // -------- 搜索框
            // 先清空搜索框
            SearchText.Text = "";
            // 光标移除文本框
            SearchText.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            // 光标聚焦
            SearchText.Focus();

            // -------- 筛选条件
            // 文件类型筛选取消选中
            ToggleButtonAutomationPeer toggleButtonAutomationPeer = new ToggleButtonAutomationPeer(_radioButtonAll);
            IToggleProvider toggleProvider = toggleButtonAutomationPeer.GetPattern(PatternInterface.Toggle) as IToggleProvider;
            toggleProvider.Toggle();

            // 精确检索
            PreciseRetrieval.IsChecked = false;
            // 匹配全词
            MatchWords.IsChecked = false;      

            // 排序类型切换为默认
            SortOptions.SelectedIndex = 0;
            // 文件名和内容
            SearchScope.SelectedIndex = 0;

            // -------- 搜索结果列表
            // 搜索结果列表清空
            SearchResultList.Items.Clear();

            // -------- 右侧预览区
            // 清空预览搜索框
            PreviewSearchText.Text = "";
            _lastPreviewSearchText = "";

            // 右侧预览区，打开文件和文件夹标记清空
            OpenFile.Tag = null;
            OpenFolder.Tag = null;

            // 滚动条回滚到最顶端
            PreviewFileContent.ScrollToHome();
            // 滚动条回滚到最顶端（图片）
            // PreviewImageScrollViewer.ScrollToHome();

            // 预览文件名清空
            PreviewFileName.Text = "";

            // 预览文件内容清空
            PreviewFileContent.Document.Blocks.Clear();

            // 预览图片清空
            PreviewImage.Source = null;

            // 预览文件类型图标清空
            PreviewFileTypeIcon.Source = null;

            // -------- 分页标签
            // 还原为第一页
            PageNow = 1;
            // 设置分页标签总条数
            this.Dispatcher.BeginInvoke(new Action(() =>
            {
                this.PageBar.Total = 0;
                this.PageBar.PageIndex = 1;
            }));

            // -------- 快捷标签
            // 隐藏上一个和下一个切换面板
            this.SwitchPreview.Visibility = Visibility.Collapsed;
            // 预览文件内容分页标记
            SwitchPreviewContentPage.Tag = 0;
            SwitchPreviewContentPage.Visibility = Visibility.Collapsed;

            // -------- 搜索参数
            _searchParam = null;

            // -------- 状态栏
            // 工作状态更新为就绪
            WorkStatus.Text = "就绪";
        }
        #endregion

        #region 列表
        /// <summary>
        /// 列表项被选中事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SearchResultList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SearchResultList.SelectedIndex == -1)
            {
                return;
            }

            // 预览切换索引标记
            this.SwitchPreview.Tag = SearchResultList.SelectedIndex;
            // 显示预览分页信息
            this.PreviewPage.Text = String.Format("{0}/{1}", this.SearchResultList.SelectedIndex + 1, SearchResultList.Items.Count);

            // 手动GC
            GC.Collect();
            GC.WaitForPendingFinalizers();

            FileInfoItem infoItem = SearchResultList.SelectedItem as FileInfoItem;
            Entity.FileInfo fileInfo = infoItem.Tag as Entity.FileInfo;

            // 根据文件类型显示图标
            PreviewFileTypeIcon.Source = FileUtil.GetFileIcon(fileInfo.FileType);
            PreviewFileName.Text = fileInfo.FileName;
            PreviewFileContent.Document.Blocks.Clear();

            // 绑定打开文件和打开路径的Tag
            OpenFile.Tag = fileInfo.FilePath;
            OpenFolder.Tag = fileInfo.FilePath.Substring(0, fileInfo.FilePath.LastIndexOf("\\"));

            // 滚动条回滚到最顶端
            PreviewFileContent.ScrollToHome();
            // 滚动条回滚到最顶端（图片）
            // PreviewImageScrollViewer.ScrollToHome();

            // 图片文件
            if (FileType.常用图片 == FileTypeUtil.GetFileType(fileInfo.FilePath))
            {
                PreviewFileContent.Visibility = Visibility.Hidden;
                PreviewImage.Visibility = Visibility.Visible;
                Thread t = new Thread(new ThreadStart(() =>
                {
                    try
                    {
                        BitmapImage bi = new BitmapImage();
                        bi.BeginInit();
                        bi.CacheOption = BitmapCacheOption.OnLoad;
                        bi.StreamSource = new MemoryStream(File.ReadAllBytes(fileInfo.FilePath));
                        bi.EndInit();
                        bi.Freeze();

                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            PreviewImage.Source = bi;
                        }));
                    }
                    catch (Exception ex)
                    {
                        log.Error(ex.Message, ex);
                        try
                        {
                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                PreviewImage.Source = null;
                            }));
                        }
                        catch { }
                    }
                }));
                t.Priority = ThreadPriority.AboveNormal;
                t.Start();
            }
            else
            {
                PreviewImage.Visibility = Visibility.Hidden;
                PreviewFileContent.Visibility = Visibility.Visible;
                // 标记当前页
                SwitchPreviewContentPage.Tag = 0;
                // 填充数据
                RichTextBoxUtil.EmptyData(PreviewFileContent);
                // 文件内容预览
                Thread t = new Thread(() =>
                {
                    try
                    {
                        // 文件内容（预览）FileInfoServiceFactory.GetFileContent(fileInfo.FilePath, true);
                        string content = fileInfo.Preview;
                        // 内容预览分页
                        List<string> previewList = SplitContent(content);
                        // 缓存预览列表，关键词列表，是否区分大小写
                        CacheUtil.Put(AppConst.CacheKey.PREVIEW_CONTENT_SPLIT_LIST_KEY, previewList);
                        CacheUtil.Put(AppConst.CacheKey.PREVIEW_FILE_INFO_KEYWORDS_KEY, fileInfo.Keywords);

                        Dispatcher.InvokeAsync(() =>
                        {

                            PreviewRefresh(previewList, 0);

                            // 显示或隐藏操作按钮
                            SwitchPreviewContentPage.Visibility = previewList.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
                        });
                    }
                    catch (Exception ex)
                    {
                        log.Error(ex.Message, ex);
                    }
                });
                t.Priority = ThreadPriority.Highest;
                t.Start();
            }
        }

        /// <summary>
        /// 分隔内容
        /// </summary>
        /// <param name="content"></param>
        /// <returns></returns>
        private List<string> SplitContent(string content)
        {
            int limit = AppConst.FILE_PREVIEW_LEN_LIMIT;
            int lenth = content.Length;
            List<string> splitList = new List<string>();
            if (lenth < limit)
            {
                splitList.Add(content);
            }
            else
            {
                int splitLenth = lenth / limit;
                for (int i = 0; i < splitLenth; i++)
                {
                    splitList.Add(content.Substring(limit * i, limit));
                }
                if (lenth % limit != 0)
                {
                    splitList.Add(content.Substring(limit * splitLenth));
                }
            }
            return splitList;
        }

        /// <summary>
        /// 刷新预览
        /// </summary>
        /// <param name="previewList">预览内容列表</param>
        /// <param name="page">当前页</param>
        private void PreviewRefresh(List<string> previewList, int page)
        {
            string preview = previewList[page];
            // 填充数据
            RichTextBoxUtil.FillingData(PreviewFileContent, preview, new SolidColorBrush(Colors.Black));

            // 显示分页信息
            PreviewContentPage.Text = string.Format("{0}/{1}", page + 1, previewList.Count);

            // 滚动条回滚到最顶端
            PreviewFileContent.ScrollToHome();
            // 滚动条回滚到最顶端（图片）
            // PreviewImageScrollViewer.ScrollToHome();

            // 获取文件名
            string fileName = PreviewFileName.Text;
            Task.Factory.StartNew(() => {
                int matchCount = IndexCore.GetMatchCount(new Entity.FileInfo()
                {
                    SearchRegion = SearchRegion.文件名和内容,
                    FileName = fileName,
                    Preview = preview,
                });
                Dispatcher.InvokeAsync(async () =>
                {
                    // 关键词高亮
                    RichTextBoxUtil.Highlighted(
                        PreviewFileContent, 
                        Colors.Red, 
                        CacheUtil.Get<List<string>>(AppConst.CacheKey.PREVIEW_FILE_INFO_KEYWORDS_KEY)
                    );
                });
            });
        }
        #endregion

        #region 界面事件

        /// <summary>
        /// 搜索域切换事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SearchScope_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            BeforeSearch();
        }
        /// <summary>
        /// 文件类型过滤器选中事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FileType_Checked(object sender, RoutedEventArgs e)
        {
            if (!"全部".Equals((sender as RadioButton).Content) && GetSearchTextKeywords().Count <= 0)
            {
                ResetSearchResult();
                return;
            }

            SearchFileType.Tag = (sender as RadioButton).Tag;

            BeforeSearch();
        }

        /// <summary>
        /// 匹配全词
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CheckChange(object sender, RoutedEventArgs e)
        {
            BeforeSearch();
        }

        /// <summary>
        /// 优化按钮
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void IndexUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            if (build)
            {
                MessageCore.ShowWarning("索引构建中，不能重复执行！");
                return;
            }
            build = true;

            ShowStatus("开始更新索引，请稍等...");

            Task.Factory.StartNew(() =>
            {
                BuildIndex(false, false);
            });
        }

        /// <summary>
        /// 重建按钮
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void IndexRebuildButton_Click(object sender, RoutedEventArgs e)
        {
            if (build)
            {
                MessageCore.ShowWarning("索引构建中，不能重复执行！");
                return;
            }
            if (CheckIndexExist(false))
            {
                var result = await MessageCore.Confirm("确定要重建索引嘛？时间可能比较久哦！", "确认提示");
                if (result == MessageBoxResult.Cancel)
                {
                    return;
                }
            }

            if (build)
            {
                MessageCore.ShowWarning("索引构建中，请稍等。");
                return;
            }
            build = true;

            ShowStatus("开始重建索引，请稍等...");

            Task.Factory.StartNew(() =>
            {
                BuildIndex(true, false);
            });
        }

        /// <summary>
        /// 搜索区双击事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void AreaInfos_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            AreaWindow areaDialog = new AreaWindow();
            areaDialog.Owner = this;
            areaDialog.Topmost = true;
            areaDialog.ShowDialog();
            
			// 不管是否修改都刷新
            InitializeAppConfig();
        }

        /// <summary>
        /// 上一个
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void BtnLast_MouseUp(object sender, MouseButtonEventArgs e)
        {
            Switch2Preview(HotKeySetting.上一个);
        }

        /// <summary>
        /// 下一个
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void BtnNext_MouseUp(object sender, MouseButtonEventArgs e)
        {
            Switch2Preview(HotKeySetting.下一个);
        }

        /// <summary>
        /// 切换预览，next为true，下一个；next为false，上一个
        /// </summary>
        /// <param name="next"></param>
        private void Switch2Preview(HotKeySetting setting)
        {
            // 当前索引 = 预览标记不为空 ? 使用标记 ： 默认值0
            int index = this.SwitchPreview.Tag != null ? int.Parse(this.SwitchPreview.Tag + "") : -1;

            // 搜索结果列表为空时，不能执行切换
            if (this.SearchResultList.Items.Count <= 0)
            {
                return;
            }

            // 下一个
            if (setting == HotKeySetting.下一个 && index < this.SearchResultList.Items.Count)
            {
                this.SearchResultList.SelectedIndex = index + 1;
            }
            // 上一个
            else if (setting == HotKeySetting.上一个 && index > 0)
            {
                this.SearchResultList.SelectedIndex = index - 1;
            }

            // 显示分页信息
            this.PreviewPage.Text = String.Format("{0}/{1}", this.SearchResultList.SelectedIndex + 1, SearchResultList.Items.Count);
        }

        /// <summary>
        /// 预览内容上一页
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void BtnLastPage_MouseUp(object sender, MouseButtonEventArgs e)
        {
            // 获取列表
            List<string> previewList = CacheUtil.Get<List<string>>(AppConst.CacheKey.PREVIEW_CONTENT_SPLIT_LIST_KEY);
            if (previewList != null)
            {
                // 获取当前页
                int page = (int)SwitchPreviewContentPage.Tag;
                if (page > 0)
                {
                    page = page - 1;
                    SwitchPreviewContentPage.Tag = page;
                }
                PreviewRefresh(previewList, page);
            }
        }

        /// <summary>
        /// 预览内容下一页
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void BtnNextPage_MouseUp(object sender, MouseButtonEventArgs e)
        {
            // 获取列表
            List<string> previewList = CacheUtil.Get<List<string>>(AppConst.CacheKey.PREVIEW_CONTENT_SPLIT_LIST_KEY);
            if (previewList != null)
            {
                // 获取当前页
                int page = (int)SwitchPreviewContentPage.Tag;
                if (page < previewList.Count - 1)
                {
                    page = page + 1;
                    SwitchPreviewContentPage.Tag = page;
                }
                PreviewRefresh(previewList, page);
            }
        }
        #endregion

        #region 辅助方法
        /// <summary>
        /// 检查索引是否需要更新
        /// </summary>
        private void IndexUpdateTask()
        {
            Task.Factory.StartNew(() =>
            {
                try
                {
                    while (AppConst.ENABLE_INDEX_UPDATE_TASK)
                    {
                        if (build)
                        {
                            log.Info("上次任务还没执行完成，跳过本次任务。");
                            return;
                        }
                        build = true;
                        // 执行索引更新，扫描新文件。
                        log.Info("开始执行索引更新检查。");
                        BuildIndex(false, true);

                        // 配置参数错误导致的bug矫正
                        if (AppConst.INDEX_UPDATE_TASK_INTERVAL <= 5)
                            AppConst.INDEX_UPDATE_TASK_INTERVAL = 5;

                        Thread.Sleep(TimeSpan.FromMinutes(AppConst.INDEX_UPDATE_TASK_INTERVAL));
                    }
                }
                catch (Exception ex)
                {
                    log.Error("索引更新任务执行错误：" + ex.Message, ex);
                }
            });
        }

        /// <summary>
        /// 检查索引是否存在
        /// </summary>
        /// <returns></returns>
        private bool CheckIndexExist(bool showWarning = true)
        {
            bool exists = Directory.Exists(AppConst.APP_INDEX_DIR);
            if (!exists)
            {
                if (showWarning)
                {
                    MessageCore.ShowWarning("首次使用，需先设置搜索区，并重建索引");
                }
            }
            return exists;
        }

        /// <summary>
        /// 构建索引
        /// </summary>
        /// <param name="isRebuild">是否重建</param>
        /// <param name="isBackground">是否后台执行，默认前台执行</param>
        private void BuildIndex(bool isRebuild, bool isBackground = false)
        {
            try
            {
                // 提示语
                string tag = isRebuild ? "重建" : "更新";

                // 1、-------- 定义总数
                // 文件总数
                int fileTotalCount = 0;
                // 更新总数
                int updateTotalCount = 0;
                // 删除总数
                int deleteTotalCount = 0;
                // 错误总数
                int errorTotalCount = 0;

                // 总任务消耗时间
                var totalTaskMark = TaskTime.StartNew();

                // 2、-------- 遍历搜索区
                List<Entity.AreaInfo> areaInfos = AreaUtil.GetEnableAreaInfoList();
                int areaInfosCount = areaInfos.Count;
                for (int i = 0; i < areaInfosCount; i++)
                {
                    Entity.AreaInfo areaInfo = areaInfos[i];

                    var singleTaskMark = TaskTime.StartNew();

                    // 不同区域，索引分开记录
                    string areaIdIndex = areaInfo.AreaId + "Index";

                    // 重建则删除全部标记
                    if (isRebuild)
                    {
                        // 重建时，删除全部标记
                        AppUtil.DeleteSection(areaIdIndex);
                    }

                    // 2.1、-------- 开始获取文件列表
                    string msg = string.Format("搜索区【{0}】，开始扫描文件...", areaInfo.AreaName);
                    log.Info(msg);
                    ShowStatus(msg);

                    // 定义全部文件列表
                    List<string> allFilePaths = new List<string>();
                    // 定义更新文件列表
                    List<string> updateFilePaths = new List<string>();
                    // 定义删除文件列表
                    List<string> deleteFilePaths = new List<string>();

                    // 2.2、-------- 获取支持的文件类型后缀（根据不同区域配置的支持文件类型查找对应的文件列表）
                    Regex fileExtRegex = new Regex(@"^.+\.(" + FileTypeUtil.ConvertToFileTypeExts(areaInfo.AreaFileTypes, "|") + ")$");

                    var scanTaskMark = TaskTime.StartNew();
                    // 扫描需要建立索引的文件列表
                    foreach (string s in areaInfo.AreaFolders)
                    {
                        log.Info("目录：" + s);
                        // 获取文件信息列表
                        FileUtil.GetAllFiles(allFilePaths, s, fileExtRegex);
                    }

                    msg = string.Format("搜索区【{0}】，文件扫描完成；文件数：{1}，耗时：{2}；开始分析需要更新的文件列表...", areaInfo.AreaName, allFilePaths.Count, scanTaskMark.ConsumeTime);
                    log.Info(msg);
                    ShowStatus(msg);

                    var analysisTaskMark = TaskTime.StartNew();
                    // 2.3、-------- 获取需要删除的文件列表
                    if (AppUtil.ReadSectionList(areaIdIndex) != null)
                    {
                        foreach (string filePath in AppUtil.ReadSectionList(areaIdIndex))
                        {
                            // 不存在，则表示文件已删除
                            if (!allFilePaths.Contains(filePath))
                            {
                                deleteFilePaths.Add(filePath);
                                AppUtil.WriteValue(areaIdIndex, filePath, null);
                            }
                        }
                    }

                    // 2.4、-------- 如果是更新操作，判断文件格式是否变化 -> 判断文件更新时间变化找到最终需要更新的文件列表
                    // 更新是才需要校验，重建是直接跳过
                    if (!isRebuild)
                    {
                        // 更新：需要更新的文件列表
                        foreach (string filePath in allFilePaths)
                        {
                            try
                            {
                                FileInfo fileInfo = new FileInfo(filePath);
                                // 当前文件修改时间
                                string lastWriteTime = fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss.ffff");
                                // 上次索引时文件修改时间标记
                                string lastWriteTimeTag = AppUtil.ReadValue(areaIdIndex, filePath);

                                // 文件修改时间不一致，说明文件已修改
                                if (!lastWriteTime.Equals(lastWriteTimeTag))
                                {
                                    updateFilePaths.Add(filePath);
                                }
                            }
                            catch { }
                        }
                    }
                    else
                    {
                        // 重建：全部文件列表
                        updateFilePaths.AddRange(allFilePaths);
                    }

                    msg = string.Format("搜索区【{0}】，文件分析完成；{1}数：{2}，删除数：{3}，耗时：{4}；开始{5}索引...", areaInfo.AreaName, tag, updateFilePaths.Count, deleteFilePaths.Count, analysisTaskMark.ConsumeTime, tag);
                    log.Info(msg);
                    ShowStatus(msg);

                    // 2.5、-------- 验证扫描文件列表是否为空（如果是更新操作，判断文件格式是否变化 -> 判断文件更新时间变化找到最终需要更新的文件列表）
                    if (updateFilePaths.Count <= 0 && deleteFilePaths.Count <= 0)
                    {
                        build = false;
                        msg = string.Format("搜索区【{0}】，无更新文件和删除文件，不{1}索引...", areaInfo.AreaName, tag);
                        log.Info(msg);
                        ShowStatus(msg);
                        continue;
                    }

                    // 后台执行时修改为最小线程单位，反之恢复为系统配置线程数
                    AppCore.SetThreadPoolSize(!isBackground);

                    // 2.6、-------- 创建索引方法
                    Entity.CreareIndexParam creareParam = new Entity.CreareIndexParam()
                    {
                        AreaId = areaInfo.AreaId,
                        AreaIndex = i,
                        AreasCount = areaInfosCount,
                        UpdateFilePaths = updateFilePaths,
                        DeleteFilePaths = deleteFilePaths,
                        IsRebuild = isRebuild,
                        Callback = ShowStatus
                    };
                    int errorCount = IndexCore.CreateIndex(creareParam);

                    // 2.7、-------- 当前区域完成日志
                    msg = string.Format("搜索区【{0}】，索引{1}完成；{2}数：{3}，删除数：{4}，错误数：{5}，共用时：{6}。", areaInfo.AreaName, tag, tag, updateFilePaths.Count, deleteFilePaths.Count, errorCount, singleTaskMark.ConsumeTime);
                    log.Info(msg);
                    ShowStatus(msg);

                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        MessageCore.ShowSuccess(msg);
                    }));


                    // 2.8、-------- 记录文件总数、更新总数、删除总数、错误总数
                    fileTotalCount = fileTotalCount + allFilePaths.Count;
                    updateTotalCount = updateTotalCount + updateFilePaths.Count;
                    deleteTotalCount = deleteTotalCount + deleteFilePaths.Count;
                    errorTotalCount = errorTotalCount + errorCount;
                }

                // 3、-------- 完成日志
                string message = string.Format("索引{0}完成。区域数：{1}，{2}数：{3}，删除数：{4}，错误数：{5}，共用时：{6}。", tag, areaInfos.Count, tag, updateTotalCount, deleteTotalCount, errorTotalCount, totalTaskMark.ConsumeTime);
                log.Info(message);
                ShowStatus(message);

                // 4、-------- 标记索引文件数量 和 最后更新时间
                AppUtil.WriteValue("AppConfig", "FileTotalCount", fileTotalCount + "");
                AppUtil.WriteValue("AppConfig", "LastIndexTime", DateTime.Now.ToString());

                // 5、-------- 构建结束
                build = false;
            }
            catch (Exception ex)
            {
                log.Error("构建索引错误：" + ex.Message, ex);

                build = false;
            }
        }

        /// <summary>
        /// 显示状态
        /// </summary>
        /// <param name="text">消息</param>
        /// <param name="percent">进度，0-100</param>
        private void ShowStatus(string text, double percent = AppConst.MAX_PERCENT)
        {
            void refresh()
            {
                WorkStatus.Text = text;
                if (percent > AppConst.MIN_PERCENT)
                {
                    WorkProgress.Value = percent;

                    TaskbarItemInfo.ProgressState = percent < AppConst.MAX_PERCENT ? System.Windows.Shell.TaskbarItemProgressState.Normal : System.Windows.Shell.TaskbarItemProgressState.None;
                    TaskbarItemInfo.ProgressValue = WorkProgress.Value / WorkProgress.Maximum;
                }
            }
            try
            {
                refresh();
            }
            catch
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    refresh();
                }));
            }
        }
        #endregion

        #region 右侧预览区域
        /// <summary>
        /// 打开文件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OpenFile_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (OpenFile.Tag != null)
            {
                string filePath = OpenFile.Tag + "";
                try
                {
                    System.Diagnostics.Process.Start(filePath);
                }
                catch (Exception ex)
                {
                    log.Error("打开文件失败：" + ex.Message, ex);
                }
            }
        }

        /// <summary>
        /// 打开文件夹
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OpenFolder_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (OpenFolder.Tag != null)
            {
                try
                {
                    System.Diagnostics.Process.Start("explorer.exe", @"" + OpenFolder.Tag);
                }
                catch (Exception ex)
                {
                    log.Error(ex.Message, ex);
                }
            }
        }
        #endregion

        #region 预览文本搜索
        /// <summary>
        /// 预览搜索文本框内容改变
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void PreviewSearchText_TextChanged(object sender, TextChangedEventArgs e)
        {
            string text = PreviewSearchText.Text.Trim();
            PreviewCleanButton.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Hidden;
        }

        /// <summary>
        /// 预览搜索清空按钮
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void PreviewCleanButton_Click(object sender, RoutedEventArgs e)
        {
            // 清理上一次的搜索关键词
            if (!string.IsNullOrEmpty(_lastPreviewSearchText))
            {
                List<string> keywords = _lastPreviewSearchText.Split(' ').ToList();
                Task.Factory.StartNew(() =>
                {
                    Dispatcher.Invoke(new Action(() =>
                    {
                        // 关键词高亮
                        RichTextBoxUtil.Highlighted(PreviewFileContent, Colors.White, keywords, true);
                    }));
                });
                _lastPreviewSearchText = "";
            }

            // 清理文本框
            PreviewSearchText.Text = "";
        }
        /// <summary>
        /// 预览搜索文本搜索按钮
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void PreviewSearchButton_Click(object sender, RoutedEventArgs e)
        {
            // 预览搜索关键词高亮
            PreviewSearchTextHighlighted();
        }

        /// <summary>
        /// 预览搜索关键词高亮
        /// </summary>
        private void PreviewSearchTextHighlighted()
        {
            // 搜索关键词
            string text = PreviewSearchText.Text.Trim();

            // 清理上一次的搜索关键词（上一次搜索关键词不为空 && 和本次搜索关键词不相同才清理）
            if (!string.IsNullOrEmpty(_lastPreviewSearchText) && !_lastPreviewSearchText.Equals(text))
            {
                List<string> keywords = _lastPreviewSearchText.Split(' ').ToList();
                Task.Factory.StartNew(() => {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        // 关键词高亮
                        RichTextBoxUtil.Highlighted(PreviewFileContent, Colors.White, keywords, true);
                    }));
                });
            }
            
            if (!string.IsNullOrEmpty(text))
            {
                _lastPreviewSearchText = text;

                List<string> keywords = text.Split(' ').ToList();
                Task.Factory.StartNew(() => {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        // 关键词高亮
                        RichTextBoxUtil.Highlighted(PreviewFileContent, Colors.DeepSkyBlue, keywords, true);
                    }));
                });
            }
        }

        /// <summary>
        /// 预览搜索文本按键
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void PreviewSearchText_PreviewKeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                // 预览搜索关键词高亮
                PreviewSearchTextHighlighted();
            }
        }
        #endregion

        #region 其他私有封装
        /// <summary>
        /// 获取文本关键词
        /// </summary>
        /// <returns></returns>
        private List<string> GetSearchTextKeywords()
        {
            string searchText = SearchText.Text.Trim();

            // 清理特殊字符

            // 申明关键词列表
            List<string> keywords = new List<string>();
            // 为空直接返回null
            if (string.IsNullOrEmpty(searchText)) return keywords;

            // 精确检索未选中
            if (PreciseRetrieval.IsChecked == false)
            {
                // 替换内置关键词
                searchText = AppConst.REGEX_BUILT_IN_SYMBOL.Replace(searchText, " ");
            }

            // 空格分词
            if (searchText.IndexOf(" ") != -1)
            {
                string[] texts = searchText.Split(' ');
                foreach (string keyword in texts)
                {
                    if (string.IsNullOrEmpty(keyword))
                    {
                        continue;
                    }
                    keywords.Add(keyword);
                }
            }
            else
            {
                // 精确检索
                if (PreciseRetrieval.IsChecked == true)
                {
                    keywords.Add(searchText);
                }
                // 通配符 || 内置字符（AND|OR|NOT）
                else if (AppConst.REGEX_SUPPORT_WILDCARDS.IsMatch(searchText))
                {
                    keywords.Add(searchText);
                }
                // 分词器分词
                else
                {
                    // 分词列表
                    List<string> segmentList = AppConst.INDEX_SEGMENTER.CutForSearch(searchText).ToList();
                    // 合并关键列表
                    keywords = keywords.Union(segmentList).ToList();
                }
            }
            return keywords;
        }

        /// <summary>
        /// 搜索前
        /// </summary>
        /// <param name="page">指定页</param>
        private void BeforeSearch(int page = 1)
        {
            // 1、---- 搜索信息预处理
            // 还原分页count
            if (page != PageNow)
            {
                PageNow = page;
                // 设置分页标签总条数
                this.Dispatcher.BeginInvoke(new Action(() =>
                {
                    this.PageBar.Total = 0;
                    this.PageBar.PageIndex = PageNow;
                }));
            }

            // 获取搜索关键词列表
            List<string> keywords = GetSearchTextKeywords();
            if (keywords.Count <= 0)
            {
                return;
            }


            // 2、---- 预览信息还原
            // 预览区打开文件和文件夹标记清空
            OpenFile.Tag = null;
            OpenFolder.Tag = null;

            // 预览文件名清空
            PreviewFileName.Text = "";

            // 预览文件内容清空
            PreviewFileContent.Document.Blocks.Clear();

            // 预览文件内容分页标记
            SwitchPreviewContentPage.Tag = 0;
            SwitchPreviewContentPage.Visibility = Visibility.Collapsed;

            // 预览图标清空
            PreviewImage.Source = null;

            // 预览文件类型图标清空
            PreviewFileTypeIcon.Source = null;

            // 预览切换标记清空
            SwitchPreview.Tag = null;


            // 3、---- 生成本次搜索时间戳
            _timestamp = Convert.ToInt64((DateTime.Now - new DateTime(1970, 1, 1, 0, 0, 0, 0)).TotalMilliseconds);


            // 4、---- 构造搜索条件并保存（保存的搜索条件需要用于数据导出）
            _searchParam = new Entity.SearchParam()
            {
                Keywords = keywords,
                FileType = (FileType)SearchFileType.Tag,
                SortType = (SortType)SortOptions.SelectedValue,
                IsPreciseRetrieval = (bool) PreciseRetrieval.IsChecked,
                IsMatchWords = (bool)MatchWords.IsChecked,
                SearchRegion = (SearchRegion)SearchScope.SelectedValue,
                PageSize = PageSize,
                PageIndex = PageNow
            };

            // 5、---- 搜索
            Search(
                _timestamp,
                _searchParam
            );
        }
        #endregion
    }
}
