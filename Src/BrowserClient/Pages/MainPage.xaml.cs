﻿using LinesBrowser;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Metadata;
using Windows.Graphics.Display;
using Windows.Networking.Sockets;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.System.Display;
using Windows.UI.Core;
using Windows.UI.Input;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;
using Windows.Web.UI.Interop;

namespace LinesBrowser
{
    /// <summary>
    /// That code needs refactoring. Now it is impossible to read and modify it.
    /// But, I don't have enough time for that right now
    /// </summary>
    public sealed partial class MainPage : Page
    {
        WebBrowserDataSource webBrowserDataSource = ConnectionHelper.Instance.webBrowserDataSource;
        Network.AudioStreamerClient audioStreamerClient = ConnectionHelper.Instance.audioStreamerClient;
        public UdpClient sendingClient;
        public UdpClient recivingClient;

        public string broadcastAddress = "255.255.255.255";
        Timer UdpDiscoveryTimer;

        private long activeTabId = 0;

        private Dictionary<long, Task> screenshotUpdateQueue = new Dictionary<long, Task>();
        private Dictionary<long, TaskCompletionSource<bool>> screenshotCompletionSources = new Dictionary<long, TaskCompletionSource<bool>>();

        private string defaultNewPageUrl = "https://google.com/";

        private bool isCanGoBack = false;
        private bool isCanGoForward = false;
        public MainPage()
        {
            this.InitializeComponent();
            if (IsMobile)
            {
                Windows.UI.ViewManagement.StatusBar statusBar = Windows.UI.ViewManagement.StatusBar.GetForCurrentView();
                _ = statusBar?.HideAsync();
            }
            ApplicationDataContainer localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
            ApplicationView.GetForCurrentView().VisibleBoundsChanged += OnVisibleBoundsChanged;

            if (localSettings.Values.ContainsKey("LastServerUrl"))
            {
                Debug.WriteLine("Has key");
                Debug.WriteLine(localSettings.Values["LastServerUrl"] as string);
                serverAddress.Text = localSettings.Values["LastServerUrl"] as string;
            }
            else
            {
                Debug.WriteLine("No known server");
            }

            InputPane inputPane = InputPane.GetForCurrentView();
            inputPane.Showing += InputPane_Showing;
            inputPane.Hiding += InputPane_Hiding;
            EntryNavBar.Width = Window.Current.Bounds.Width;
            Window.Current.SizeChanged += Current_SizeChanged;
            Canvas.SetTop(EntryNavBar, Window.Current.Bounds.Height - EntryNavBar.Height);

            MoreSettingsGrid.Width = Window.Current.Bounds.Width;
            OverlaySettingsLinksGrid.Width = MoreSettingsGrid.Width;
            NavbarGrid.Width = MoreSettingsGrid.Width;
            Canvas.SetTop(MoreSettingsGrid, Window.Current.Bounds.Height - EntryNavBar.Height - MoreSettingsGrid.Height);
            MoreSettingsAppBar.Loaded += MoreSettingsAppBar_Loaded;

            Canvas.SetTop(NavbarGrid, Window.Current.Bounds.Height - NavbarGrid.Height);
            SetupTabWidth();
            ConnectHandlers();

            SystemNavigationManager.GetForCurrentView().AppViewBackButtonVisibility
                = AppViewBackButtonVisibility.Visible;

            SystemNavigationManager.GetForCurrentView().BackRequested += (s, e) =>
            {
                if (isCanGoBack)
                {
                    webBrowserDataSource.NavigateBack();
                    TogglePageLoadingMode(true);
                    e.Handled = true;
                }
                e.Handled = true;
            };

            if (ApiInformation.IsApiContractPresent("Windows.Phone.PhoneContract", 1, 0))
            {
                Windows.Phone.UI.Input.HardwareButtons.BackPressed += (s, e) => 
                {
                    if (isCanGoBack)
                    {
                        webBrowserDataSource.NavigateBack();
                        TogglePageLoadingMode(true);
                        e.Handled = true;
                    }
                    e.Handled = true;
                };
            }


            DisplayRequest displayRequest = new DisplayRequest();
            displayRequest.RequestActive();
        }        
     
        
        private void OnVisibleBoundsChanged(ApplicationView sender, object args)
        {
            _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, UpdateNavbarPosition);
        }

        private void UpdateNavbarPosition()
        {
            var visible = ApplicationView.GetForCurrentView().VisibleBounds;

            double navHeight = NavbarGrid.ActualHeight;

            double top = visible.Height - navHeight;

            Canvas.SetTop(NavbarGrid, top);

            test.Height = visible.Height - NavbarGrid.Height;
            test.Width = visible.Width;
        }

        const double MINTABWIDTH = 150;
        private double DefaultTabWidth = MINTABWIDTH;
        private void SetupTabWidth()
        {
            double _tabGridContentWidth = (Window.Current.Bounds.Width - 20);
            double row = Math.Floor(_tabGridContentWidth / MINTABWIDTH);
            double newWidth = _tabGridContentWidth / row - 4.5;
            DefaultTabWidth = newWidth;
            foreach (var item in TabsList.Items)
            {
                var container = TabsList.ContainerFromItem(item) as GridViewItem;
                if (container != null)
                {
                    var border = FindChild<Border>(container);
                    Debug.WriteLine($"{border.Width}, {newWidth}");
                    if (border != null)
                    {
                        border.Width = newWidth;
                        border.Height = newWidth;
                    }
                }
            }
        }
        private T FindChild<T>(DependencyObject parent) where T : DependencyObject
        {
            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T foundChild)
                {
                    return foundChild;
                }
                var result = FindChild<T>(child);
                if (result != null)
                {
                    return result;
                }
            }
            return null;
        }
        private void MoreSettingsAppBar_Loaded(object sender, RoutedEventArgs e)
        {
            var visible = ApplicationView.GetForCurrentView().VisibleBounds;

            MoreSettingsGrid.Height = MoreSettingsAppBar.ActualHeight;
            OverlaySettingsLinksGrid.Height = SettingsLinksScrollView.ActualHeight;

            Canvas.SetTop(MoreSettingsGrid, visible.Height - EntryNavBar.Height - MoreSettingsGrid.Height);
            Canvas.SetTop(OverlaySettingsLinksGrid, visible.Height - EntryNavBar.Height - MoreSettingsGrid.Height -
                OverlaySettingsLinksGrid.Height);
            Canvas.SetTop(NavbarGrid, visible.Height - EntryNavBar.Height);

            OverlayMoreSettingsCanvas.Visibility = Visibility.Collapsed;
            OverlaySettingsLinks.Visibility = Visibility.Collapsed;
            SettingsLinksGridHide.To = SettingsLinksScrollView.ActualHeight+50;
        }
        private void Current_SizeChanged(object sender, Windows.UI.Core.WindowSizeChangedEventArgs e)
        {
            var visible = ApplicationView.GetForCurrentView().VisibleBounds;

            Canvas.SetTop(EntryNavBar, visible.Height - EntryNavBar.Height);
            EntryNavBar.Width = visible.Width;
            MoreSettingsGrid.Width = visible.Width;
            NavbarGrid.Width = visible.Width;
            ScreenshotImage.Width = visible.Width;
            OverlaySettingsLinksGrid.Width = MoreSettingsGrid.Width;

            OverlayFocusRectangle.Height = visible.Height;
            OverlayFocusRectangle.Width = visible.Width;

            TabsList.Width = visible.Width;

            Canvas.SetTop(NavbarGrid, visible.Height - NavbarGrid.Height);

            SetupTabWidth();
        }
        private void InputPane_Showing(InputPane sender, InputPaneVisibilityEventArgs args)
        {
            var visible = ApplicationView.GetForCurrentView().VisibleBounds;
            Canvas.SetTop(
                EntryNavBar, 
                Window.Current.Bounds.Height - (Window.Current.Bounds.Height - visible.Height) - args.OccludedRect.Height - EntryNavBar.ActualHeight
                );
            EntryNavBar.Width = Window.Current.Bounds.Width; 
            args.EnsuredFocusedElementInView = true; 
        }

        private void InputPane_Hiding(InputPane sender, InputPaneVisibilityEventArgs args)
        {
            EntryNavBar.Width = Window.Current.Bounds.Width;
            Canvas.SetTop(EntryNavBar, Window.Current.Bounds.Height - 50);
        }
        private void LoseFocus(object sender)
        {
            var control = sender as Control;
            var isTabStop = control.IsTabStop;
            control.IsTabStop = false;
            control.IsEnabled = false;
            control.IsEnabled = true;
            control.IsTabStop = isTabStop;
        }
        private void TextBox_KeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                var url = urlField.Text;
                urlViewField.Text = url;
                webBrowserDataSource.Navigate(url);
                TogglePageLoadingMode(true);
                e.Handled = true; LoseFocus(sender);
            }
        }
        public static bool IsMobile
        {
            get
            {
                var qualifiers = Windows.ApplicationModel.Resources.Core.ResourceContext.GetForCurrentView().QualifierValues;
                return (qualifiers.ContainsKey("DeviceFamily") && qualifiers["DeviceFamily"] == "Mobile");
            }
        }

        private void Page_SizeChanged(object sender, Windows.UI.Xaml.SizeChangedEventArgs e)
        {
           
        }

        private void Test_PointerPressed(object sender, Windows.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            var x = e.GetCurrentPoint(null).Position.X / ScaleRect.ActualWidth;
            var y = e.GetCurrentPoint(null).Position.Y / ScaleRect.ActualHeight;
            webBrowserDataSource?.TouchDown(new Point(x, y), e.Pointer.PointerId);
        }

        private void Test_PointerReleased(object sender, Windows.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            var x = e.GetCurrentPoint(null).Position.X / ScaleRect.ActualWidth;
            var y = e.GetCurrentPoint(null).Position.Y / ScaleRect.ActualHeight;
            webBrowserDataSource?.TouchUp(new Point(x, y), e.Pointer.PointerId);
        }

        private void Test_PointerMoved(object sender, Windows.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            var x = e.GetCurrentPoint(null).Position.X / ScaleRect.ActualWidth;
            var y = e.GetCurrentPoint(null).Position.Y / ScaleRect.ActualHeight;
            webBrowserDataSource?.TouchMove(new Point(x, y), e.Pointer.PointerId);
        }
        private void Button_Click(object sender, RoutedEventArgs e)
        {
            Connect(serverAddress.Text.Replace("tcp://", "ws://"), audioAddress.Text.Replace("tcp://", "ws://"));
            ConnectPage.Visibility = Visibility.Collapsed;
        }

        private void ConnectHandlers()
        {
            if (webBrowserDataSource == null)
            {
                return;
            }
            webBrowserDataSource.FrameReceived += (sender, bitmap) =>
            {
                _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    test.Source = bitmap;
                    test.Visibility = Visibility.Visible;
                    ScreenshotScrollViewer.Visibility = Visibility.Collapsed;
                });
            };

            webBrowserDataSource.FullPageScreenshotReceived += (sender, screenshot) =>
            {
                _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    ScreenshotImage.Source = screenshot;
                    ScreenshotScrollViewer.Visibility = Visibility.Visible;
                    test.Visibility = Visibility.Collapsed;
                });
            };
            webBrowserDataSource.ServerSendComplete += (sender, state) =>
            {
                TogglePageLoadingMode(false);
            };
            webBrowserDataSource.TextPacketReceived += (s, o) =>
            {
                switch (o.PType)
                {
                    case TextPacketType.NavigatedUrl:
                        urlField.Text = o.text;
                        urlViewField.Text = o.text;
                        webBrowserDataSource.SizeChange(new Size { Width = ScaleRect.ActualWidth, Height = ScaleRect.ActualHeight });
                        break;

                    case TextPacketType.OpenPages:
                        _ = UpdateScreenshotAsync(activeTabId);
                        var pages = o.text.Split(';').ToList();
                        var lastPage = pages.Last().Split('|').ToList()[1];
                        UpdateTabsGrid(pages);
                        long _lastPage = 000000;

                        Int64.TryParse(lastPage, out _lastPage);
                        webBrowserDataSource.SetActivePage(_lastPage);
                        activeTabId = _lastPage;
                        break;

                    case TextPacketType.EditOpenTabTitle:
                        var splitUrl = o.text.Split('|').ToList();
                        if (splitUrl.Count >= 2)
                        {
                            Int64.TryParse(splitUrl[1], out long id);
                            var title = splitUrl[0];

                            var tabToUpdate = tabs.FirstOrDefault(tab => tab.Id == id);
                            if (tabToUpdate != null)
                            {
                                tabToUpdate.Url = title;
                            }

                        }
                        break;

                    case TextPacketType.TextInputContent:
                        NavbarGrid.Visibility = Visibility.Collapsed;
                        TextInput.Visibility = Visibility.Visible;
                        Debug.WriteLine($"TEXT > {o.text}");
                        websiteTextBox.Text = o.text?? "";
                        websiteTextBox.Select(websiteTextBox.Text.Length, 0);
                        websiteTextBox.Focus(FocusState.Programmatic);
                        break;

                    case TextPacketType.TextInputSend:
                        break;

                    case TextPacketType.TextInputCancel:
                        TextInput.Visibility = Visibility.Collapsed;
                        NavbarGrid.Visibility = Visibility.Visible;
                        websiteTextBox.Text = "";
                        break;
                    case TextPacketType.LoadingStateChanged:
                        if (o.text == "LOADING")
                        {
                            TogglePageLoadingMode(true);
                            webBrowserDataSource.isServerLoadingComplete = false;
                        }
                        else
                        {
                            webBrowserDataSource.isServerLoadingComplete = true;
                        }
                        break;
                    case TextPacketType.IsClientCanSendGoBackRequest:
                        if (o.text == "true")
                        {
                            isCanGoBack = true;
                            BackButton.IsEnabled = isCanGoBack;
                        }
                        else
                        {
                            isCanGoBack = false;
                            BackButton.IsEnabled = isCanGoBack;
                        }
                        break;
                    case TextPacketType.IsClientCanSendGoForwardRequest:
                        if (o.text == "true")
                        {
                            isCanGoForward = true;
                            ForwardButton.IsEnabled = isCanGoForward;
                        }
                        else
                        {
                            isCanGoForward = false;
                            ForwardButton.IsEnabled = isCanGoForward;
                        }
                        break;
                }
            };
            webBrowserDataSource.TabImageSendComplete += (s, o) =>
            {
                if (screenshotCompletionSources.ContainsKey(o))
                {
                    screenshotCompletionSources[o].TrySetResult(true);
                }
            };
            ConnectPage.Visibility = Visibility.Collapsed;
            var visible = ApplicationView.GetForCurrentView().VisibleBounds;
            string size = $"{visible.Width}x{visible.Height-NavbarGrid.Height}";
            webBrowserDataSource.RequestNewScreenshot(size);
        }

        public void Connect(string endpoint, string audioEndpoint)
        {
            ConnectionHelper.Instance.Connect(endpoint, audioEndpoint);

            webBrowserDataSource = ConnectionHelper.Instance.webBrowserDataSource;
            ConnectHandlers();

            webBrowserDataSource.SendGetTabsRequest();

            audioStreamerClient = ConnectionHelper.Instance.audioStreamerClient;
        }

        private void WebsiteTextBox_KeyUp(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            // webBrowserDataSource.SendKey(e);

            string inputChar = null;
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                webBrowserDataSource.SendKeyCommand("Enter");
                return;
            }
            else if (e.Key == Windows.System.VirtualKey.Back)
            {
                webBrowserDataSource.SendKeyCommand("Backspace");
                return;
            } 
            else if (e.Key != Windows.System.VirtualKey.Shift && e.Key != Windows.System.VirtualKey.Control)
            {
                var tb = sender as TextBox;
                Debug.WriteLine($"tb is {tb}, {tb.Text.Length}");
                if (tb != null && tb.Text.Length > 0)
                {
                    inputChar = tb.Text.Last().ToString();
                }
            }
            Debug.WriteLine($"e.Key {e.Key}");
            if (!string.IsNullOrEmpty(inputChar))
            {
                var shift = Window.Current.CoreWindow.GetKeyState(Windows.System.VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down);
                var ctrl = Window.Current.CoreWindow.GetKeyState(Windows.System.VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);
                var alt = Window.Current.CoreWindow.GetKeyState(Windows.System.VirtualKey.Menu).HasFlag(CoreVirtualKeyStates.Down);

                var inputLanguage = Windows.Globalization.Language.CurrentInputMethodLanguageTag;

                var packet = new KeyCharPacket
                {
                    JSONData = inputChar,
                    Shift = shift,
                    Ctrl = ctrl,
                    Alt = alt,
                    Layout = inputLanguage
                };

                var json = Newtonsoft.Json.JsonConvert.SerializeObject(packet);
                Debug.WriteLine($"JSON {json.ToString()}");
                webBrowserDataSource.SendChar(json);
            }
        }
        private void WebsiteTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            
        }
        private void SendText_Click(object sender, RoutedEventArgs e)
        {
            webBrowserDataSource.SendText(websiteTextBox.Text);
            TextInput.Visibility = Visibility.Collapsed;
            NavbarGrid.Visibility = Visibility.Visible;
            websiteTextBox.Text = "";
        }

        private void MainGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {

        }

        private void Browser_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            var bounds = ApplicationView.GetForCurrentView().VisibleBounds;
            var scaleFactor = DisplayInformation.GetForCurrentView().RawPixelsPerViewPixel;
            var size = new Size(bounds.Width * scaleFactor, bounds.Height * scaleFactor);
            Debug.WriteLine("AD"+ScaleRect.ActualWidth + " " + ScaleRect.ActualHeight);

            Debug.WriteLine("SIZE!!!!");
            if (webBrowserDataSource != null)
            {
                var s = e.NewSize;
                s.Width = ScaleRect.ActualWidth;
                s.Height = ScaleRect.ActualHeight;

                webBrowserDataSource.SizeChange(s);
            }

        }
        public bool discovering = false;

        DatagramSocket serverDatagramSocket;

        private void DiscoverBtn_Click(object sender, RoutedEventArgs e)
        {
            
            //TODO:
            //1336 & 1337 for UDP ports, 5454X is out of specon UWP?
            int udpPort = 54545;
            int udpRecPort = 54546;


            ConnectPage.Visibility = Visibility.Collapsed;
            DiscoveryPage.Visibility = Visibility.Visible;

            sendingClient = new UdpClient(udpPort);
            sendingClient.EnableBroadcast = true;


            recivingClient = new UdpClient(udpRecPort);
            


            //3 seconds
            UdpDiscoveryTimer = new Timer(state =>
            {
                try
                {
                    //datagram discovery, we broadcast that we WANT an adress
                    var packet = new DiscoveryPacket
                    {
                        PType = DiscoveryPacketType.AddressRequest,
                    };
                    var rawPacket = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(packet));
                    sendingClient.SendAsync(rawPacket, rawPacket.Length, new System.Net.IPEndPoint(IPAddress.Parse("255.255.255.255"), udpPort));
                }
                catch (Exception) { }
            }, null, 0, 3000);

            discovering = true;

            serverDatagramSocket = new Windows.Networking.Sockets.DatagramSocket();

            // The ConnectionReceived event is raised when connections are received.
            serverDatagramSocket.MessageReceived += ServerDatagramSocket_MessageReceived;
        
            // Start listening for incoming TCP connections on the specified port. You can specify any port that's not currently in use.
            serverDatagramSocket.BindServiceNameAsync("1337");

        }

        private void ServerDatagramSocket_MessageReceived(DatagramSocket sender, DatagramSocketMessageReceivedEventArgs args)
        {
            string request;
            using (DataReader dataReader = args.GetDataReader())
            {
                request = dataReader.ReadString(dataReader.UnconsumedBufferLength).Trim();
            }
            Debug.WriteLine(request);

            var packet = JsonConvert.DeserializeObject<DiscoveryPacket>(request);

            switch (packet.PType)
            {
                case DiscoveryPacketType.AddressRequest:
                    break;
                case DiscoveryPacketType.ACK:
                    Debug.WriteLine("ws://" + packet.ServerAddress + ":8081");

                    _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        //replace with a connect function
                        UdpDiscoveryTimer.Dispose();
                        serverDatagramSocket.Dispose();


                        Connect("ws://" + packet.ServerAddress + ":8081", "ws://" + packet.ServerAddress + ":8082");
                        /*
                        ds = new WebBrowserDataSource();
                        ds.DataRecived += (s, o) =>
                        {
                            test.Source = ConvertToBitmapImage(o).Result;
                            // ds.ACKRender();
                        };
                        */
                        ConnectPage.Visibility = Visibility.Collapsed;
                        DiscoveryPage.Visibility = Visibility.Collapsed;
                        NavbarGrid.Visibility = Visibility.Visible;
                        // ds.StartRecive("ws://" + packet.ServerAddress + ":8081");

                    });
                    break;
                default:
                    break;
            }
        }

        private void NavigateBack_Click(object sender, RoutedEventArgs e)
        {
            webBrowserDataSource.NavigateBack();
            TogglePageLoadingMode(true);
        }

        private void NavigateForward_Click(object sender, RoutedEventArgs e)
        {
            webBrowserDataSource.NavigateForward();
            TogglePageLoadingMode(true);
        }

        private void urlViewField_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            ToggleSettingsOverlay(false);
            
            EntryNavBar.Visibility = Visibility.Visible;
            urlField.Focus(FocusState.Programmatic);
            urlField.SelectAll();
            MainNavBar.Visibility = Visibility.Collapsed;
            OverlayFocus.Visibility = Visibility.Visible;
        }

        private void urlField_LostFocus(object sender, RoutedEventArgs e)
        {
            EntryNavBar.Visibility = Visibility.Collapsed;
            MainNavBar.Visibility = Visibility.Visible;
            OverlayFocus.Visibility = Visibility.Collapsed;
        }

        private void ToggleSettingsOverlay(bool state)
        {
            if (!state)
            {
                OverlayMoreSettingsCanvas.Visibility = Visibility.Collapsed;
                var visible = ApplicationView.GetForCurrentView().VisibleBounds;
                Canvas.SetTop(OverlaySettingsLinksGrid, visible.Height - EntryNavBar.Height - OverlaySettingsLinksGrid.Height);

                SettingsLinksGridStoryboardShow.Stop();
                SettingsLinksGridStoryboardHide.Begin();

                OverlayFocus.Visibility = Visibility.Collapsed;
            } 
            else
            {
                OverlayMoreSettingsCanvas.Visibility = Visibility.Visible;
                OverlaySettingsLinks.Visibility = Visibility.Visible;
                var visible = ApplicationView.GetForCurrentView().VisibleBounds;
                Canvas.SetTop(OverlaySettingsLinksGrid, visible.Height - EntryNavBar.Height - MoreSettingsGrid.Height -
                    OverlaySettingsLinksGrid.Height);


                SettingsLinksGridStoryboardHide.Stop();
                SettingsLinksGridStoryboardShow.Begin();

                MoreSettingsGrid.Height = MoreSettingsAppBar.ActualHeight;
                Canvas.SetTop(MoreSettingsGrid, visible.Height - EntryNavBar.Height - MoreSettingsGrid.Height);

                OverlayFocus.Visibility = Visibility.Visible;
            }
        }

        private void More_Click(object sender, RoutedEventArgs e)
        {
            if (OverlayMoreSettingsCanvas.Visibility == Visibility.Visible)
            {
                ToggleSettingsOverlay(false);
            }
            else
            {
                ToggleSettingsOverlay(true);
            }
        }

        private void ToggleMode(bool? showScreenshot)
        {
            if (showScreenshot == true)
            {
                ScreenshotScrollViewer.Visibility = Visibility.Visible;
                test.Visibility = Visibility.Collapsed;
                webBrowserDataSource.StaticUpdateMode = true;
            }
            else
            {
                ScreenshotScrollViewer.Visibility = Visibility.Collapsed;
                test.Visibility = Visibility.Visible;
                webBrowserDataSource.StaticUpdateMode = false;
            }
        }

        private void StaticModeToggleButton_Click(object sender, RoutedEventArgs e)
        {
            bool isStaticMode = StaticModeToggleButton.IsChecked == true;

            var mode = isStaticMode ? "Static" : "Dynamic";
            webBrowserDataSource.SendModeChange(mode);

            ToggleMode(isStaticMode);

        }

        private void SettingsLinksGridHide_Completed(object sender, object e)
        {
            OverlaySettingsLinks.Visibility = Visibility.Collapsed;
        }

        private void ViewPages_Click(object sender, RoutedEventArgs e)
        {
            ToggleSettingsOverlay(false);
            if (TabsGrid.Visibility == Visibility.Visible)
            {
                TabsGrid.Visibility = Visibility.Collapsed;
                browser.Visibility = Visibility.Visible;
            }
            else
            {
                if (tabs.Count == 0)
                {
                    webBrowserDataSource.SendGetTabsRequest();
                }
                if (tabs.Count > 0)
                {
                    if (activeTabId != 0)
                    {
                        _ = UpdateScreenshotAsync(activeTabId);

                    }
                }
                TabsGrid.Visibility = Visibility.Visible;
                browser.Visibility = Visibility.Collapsed;
                TabsList.UpdateLayout();
                NavBarStoryboardShow.Stop();
                NavBarStoryboardHide.Begin();
                SetupTabWidth();

            }
        }

        private void OverlayFocus_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            ToggleSettingsOverlay(false);
            EntryNavBar.Visibility = Visibility.Collapsed;
            MainNavBar.Visibility = Visibility.Visible;
        }

        private void HomePageButton_Click(object sender, RoutedEventArgs e)
        {
            string url = "google.com/";
            urlViewField.Text = url;
            webBrowserDataSource.Navigate(url);
            ToggleSettingsOverlay(false);
            TogglePageLoadingMode(true);
        }

        public void TogglePageLoadingMode(bool state)
        {
            if (state)
            {
                PageLoadingRing.Visibility = Visibility.Visible;
                PageLoadingRing.IsActive = true;
                ConnectionPreviewButton.Visibility = Visibility.Collapsed;
            } 
            else
            {
                PageLoadingRing.Visibility = Visibility.Collapsed;
                PageLoadingRing.IsActive = false;
                ConnectionPreviewButton.Visibility = Visibility.Visible;

            }
        }
        private ObservableCollection<TabItem> tabs = new ObservableCollection<TabItem>();
        public class TabItem : INotifyPropertyChanged
        {
            private string url;

            public long Id { get; set; }

            public string Url
            {
                get => url;
                set
                {
                    if (url != value)
                    {
                        url = value;
                        OnPropertyChanged(nameof(Url));
                    }
                }
            }
            private BitmapImage screenshot;

            public BitmapImage Screenshot 
            {
                get => screenshot;
                set
                {
                    if (screenshot != value)
                    {
                        screenshot = value;
                        OnPropertyChanged(nameof(Screenshot));
                    }
                }
            }

            public event PropertyChangedEventHandler PropertyChanged;

            protected virtual void OnPropertyChanged(string propertyName)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }
        private void TabsList_ItemClick(object sender, ItemClickEventArgs e)
        {
            var clickedTab = e.ClickedItem as TabItem;
            if (clickedTab != null)
            {
                webBrowserDataSource.SetActivePage(clickedTab.Id);
                activeTabId = clickedTab.Id;

                _ = UpdateScreenshotAsync(clickedTab.Id);
                TabsPanelHide();
                var visible = ApplicationView.GetForCurrentView().VisibleBounds;
                string size = $"{visible.Width}x{visible.Height - NavbarGrid.Height}";
                webBrowserDataSource.RequestNewScreenshot(size);
            }
        }

        private void TabsPanelHide()
        {
            TabsGrid.Visibility = Visibility.Collapsed;
            browser.Visibility = Visibility.Visible;
            NavbarGrid.Visibility = Visibility.Visible;
            NavBarStoryboardHide.Stop();
            NavBarStoryboardShow.Begin();
        }

        private async void UpdateTabsGrid(List<string> urls)
        {
            tabs.Clear();

            foreach (var url in urls)
            {
                var splitUrl = url.Split('|').ToList();
                Int64.TryParse(splitUrl[1], out long id);
                var title = splitUrl[0]; 
                tabs.Add(new TabItem
                {
                    Id = id,
                    Url = title,
                    Screenshot = await GetScreenshotForUrl(id), 
                });
            }
            TabsList.ItemsSource = tabs;

            TabsList.UpdateLayout();

            foreach (var item in TabsList.Items)
            {
                var container = TabsList.ContainerFromItem(item) as GridViewItem;
                if (container != null)
                {
                    var border = FindChild<Border>(container);
                    if (border != null)
                    {
                        border.Width = DefaultTabWidth;
                        border.Height = DefaultTabWidth;
                    }
                }
            }
        }

        private async Task<BitmapImage> GetScreenshotForUrl(long tabId)
        {
            try
            {

                var folder = await Windows.Storage.ApplicationData.Current.LocalFolder.GetFolderAsync("TabsScreenshots");
                var file = await folder.GetFileAsync($"{tabId}.png");

                using (var stream = await file.OpenAsync(FileAccessMode.Read))
                {
                    var bitmap = new BitmapImage();
                    bitmap.SetSource(stream);
                    return bitmap;
                }
            }
            catch
            {
                return new BitmapImage(new Uri("ms-appx:///Assets/placeholder.png"));
            }
        }

        private async Task UpdateScreenshotAsync(long tabId)
        {
            if (screenshotUpdateQueue.ContainsKey(tabId))
            {
                return; 
            }

            var tcs = new TaskCompletionSource<bool>();
            screenshotCompletionSources[tabId] = tcs;

            var updateTask = Task.Run(async () =>
            {
                try
                {
                    webBrowserDataSource.RequestTabScreenshot(tabId);

                    var completed = await Task.WhenAny(tcs.Task, Task.Delay(5000)) == tcs.Task;

                    if (completed)
                    {

                        await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
                        {
                            for (int i = 0; i < tabs.Count; i++)
                            {
                                if (tabs[i].Id == tabId)
                                {
                                    Debug.WriteLine($"Updating image for tab {tabId}");
                                    tabs[i].Screenshot = await GetScreenshotForUrl(tabId);
                                }
                            }
                        });
                    }
                    else
                    {
                        Debug.WriteLine($"Timeout while waiting for screenshot of tab {tabId}");
                    }
                }
                catch (Exception e)
                {
                    Debug.WriteLine($"Unexpected error: {e}");
                }
                finally
                {
                    screenshotCompletionSources.Remove(tabId);
                }
            });

            screenshotUpdateQueue[tabId] = updateTask;

            try
            {
                await updateTask;
            }
            finally
            {
                screenshotUpdateQueue.Remove(tabId);
            }
        }

        private void CloseTabsPageButton_Click(object sender, RoutedEventArgs e)
        {
            TabsPanelHide();

        }

        private void SettingsPageButton_Click(object sender, RoutedEventArgs e)
        {
            OverlayFocus.Visibility = Visibility.Collapsed;
            Frame.Navigate(typeof(SettingsPage));
        }

        private void NavBarHide_Completed(object sender, object e)
        {
            NavbarGrid.Visibility = Visibility.Collapsed;
        }

        private void CloseTabButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as FrameworkElement;
            if (button == null)
                return;

            var item = button.DataContext as TabItem; 

            if (item != null)
            {
                webBrowserDataSource.CloseTab(item.Id);
                tabs.Remove(item);
            }
        }

        private void AddNewTabButton_Click(object sender, RoutedEventArgs e)
        {
            TabsPanelHide();
            webBrowserDataSource.CreateNewTabWithUrl(defaultNewPageUrl);
        }
        private async Task ShowErrorDialogAsync(string title, string message)
        {
            await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(
                Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
                {
                    var dialog = new Windows.UI.Xaml.Controls.ContentDialog
                    {
                        Title = title,
                        Content = message,
                        CloseButtonText = "OK"
                    };
                    await dialog.ShowAsync();
                });
        }
    }
}
