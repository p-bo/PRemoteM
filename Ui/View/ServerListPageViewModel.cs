﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Newtonsoft.Json;
using PRM.Model;
using PRM.Model.Protocol;
using PRM.Model.Protocol.Base;
using PRM.Resources.Icons;
using PRM.Service;
using PRM.Utils;
using PRM.Utils.mRemoteNG;
using PRM.View.Settings;
using Shawn.Utils;
using Shawn.Utils.Interface;
using Shawn.Utils.Wpf;
using Shawn.Utils.Wpf.FileSystem;
using Stylet;


namespace PRM.View
{
    public partial class ServerListPageViewModel : NotifyPropertyChangedBase
    {
        public PrmContext Context { get; }

        #region properties

        public bool ListPageIsCardView
        {
            get => IoC.Get<SettingsPageViewModel>().ListPageIsCardView;
            set
            {
                if (IoC.Get<SettingsPageViewModel>().ListPageIsCardView != value)
                {
                    IoC.Get<SettingsPageViewModel>().ListPageIsCardView = value;
                    RaisePropertyChanged();
                }
            }
        }

        private ProtocolBaseViewModel? _selectedServerViewModelListItem = null;
        public ProtocolBaseViewModel? SelectedServerViewModelListItem
        {
            get => _selectedServerViewModelListItem;
            set => SetAndNotifyIfChanged(ref _selectedServerViewModelListItem, value);
        }

        private ObservableCollection<ProtocolBaseViewModel> _serverListItems = new ObservableCollection<ProtocolBaseViewModel>();
        public ObservableCollection<ProtocolBaseViewModel> ServerListItems
        {
            get => _serverListItems;
            set
            {
                _serverListItems = new ObservableCollection<ProtocolBaseViewModel>(GetOrderedVmProtocolServers(value, ServerOrderBy));
                ServerListItems.CollectionChanged += (sender, args) =>
                {
                    ServerListItems = new ObservableCollection<ProtocolBaseViewModel>(ServerListItems);
                };
                RaisePropertyChanged();
            }
        }
        public int SelectedCount => ServerListItems.Count(x => x.IsSelected);

        private EnumServerOrderBy _serverOrderBy = EnumServerOrderBy.IdAsc;
        public EnumServerOrderBy ServerOrderBy
        {
            get => _serverOrderBy;
            set
            {
                if (SetAndNotifyIfChanged(ref _serverOrderBy, value))
                {
                    IoC.Get<LocalityService>().ServerOrderBy = value;
                }
            }
        }

        public bool IsSelectedAll
        {
            get
            {
                return ServerListItems.All(x => x.IsSelected);
            }
            set
            {
                foreach (var vmServerCard in ServerListItems)
                {
                    vmServerCard.IsSelected = value;
                }
                RaisePropertyChanged();
            }
        }

        public bool IsMultipleSelected => ServerListItems.Count(x => x.IsSelected) > 0;

        #endregion

        public ServerListPageViewModel(PrmContext context)
        {
            Context = context;
            Context.AppData.VmItemListDataChanged += RebuildVmServerList;

            if (GlobalEventHelper.OnRequestDeleteServer == null)
                GlobalEventHelper.OnRequestDeleteServer += id =>
                {
                    if (MessageBoxResult.Yes == MessageBox.Show(
                        IoC.Get<ILanguageService>().Translate("confirm_to_delete_selected"),
                        IoC.Get<ILanguageService>().Translate("messagebox_title_warning"), MessageBoxButton.YesNo,
                        MessageBoxImage.Question, MessageBoxResult.None))
                    {
                        Context.AppData.DeleteServer(id);
                    }
                };
        }

        private void RebuildVmServerList()
        {
            Execute.OnUIThread(() =>
            {
                if (Context?.AppData?.VmItemList == null) return;
                foreach (var vs in Context.AppData.VmItemList)
                {
                    try
                    {
                        vs.PropertyChanged -= VmServerPropertyChanged;
                    }
                    finally
                    {
                        vs.PropertyChanged += VmServerPropertyChanged;
                    }
                }
                _serverListItems = new ObservableCollection<ProtocolBaseViewModel>(Context.AppData.VmItemList);

                if (string.IsNullOrEmpty(SelectedTabName) == false
                    && false == Context.AppData.TagList.Any(x => String.Equals(x.Name, SelectedTabName, StringComparison.CurrentCultureIgnoreCase)))
                {
                    SelectedTabName = TabAllName;
                }
                else
                {
                    CalcVisibleByFilter();
                }

                RaisePropertyChanged(nameof(IsMultipleSelected));
                RaisePropertyChanged(nameof(IsSelectedAll));
                RaisePropertyChanged(nameof(SelectedCount));
            });
        }

        private void VmServerPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ProtocolBaseViewModel.IsSelected))
            {
                RaisePropertyChanged(nameof(IsMultipleSelected));
                RaisePropertyChanged(nameof(IsSelectedAll));
                RaisePropertyChanged(nameof(SelectedCount));
            }
        }

        private static IEnumerable<ProtocolBaseViewModel> GetOrderedVmProtocolServers(IEnumerable<ProtocolBaseViewModel> servers, EnumServerOrderBy orderBy)
        {
            if (!(servers?.Count() > 0)) return new List<ProtocolBaseViewModel>();

            switch (orderBy)
            {
                case EnumServerOrderBy.ProtocolAsc:
                    return servers.OrderBy(x => x.Server.Protocol).ThenBy(x => x.Server.Id);

                case EnumServerOrderBy.ProtocolDesc:
                    return servers.OrderByDescending(x => x.Server.Protocol).ThenBy(x => x.Server.Id);

                case EnumServerOrderBy.NameAsc:
                    return servers.OrderBy(x => x.Server.DisplayName).ThenBy(x => x.Server.Id);

                case EnumServerOrderBy.NameDesc:
                    return servers.OrderByDescending(x => x.Server.DisplayName).ThenBy(x => x.Server.Id);

                case EnumServerOrderBy.AddressAsc:
                    return servers.OrderBy(x => x.Server.SubTitle).ThenBy(x => x.Server.Id);

                case EnumServerOrderBy.AddressDesc:
                    return servers.OrderByDescending(x => x.Server.SubTitle).ThenBy(x => x.Server.Id);

                default:
                    return servers.OrderBy(x => x.Server.Id);
            }
        }

        public void CalcVisibleByFilter()
        {
            var keyword = IoC.Get<MainWindowViewModel>().FilterString;
            var tmp = TagAndKeywordEncodeHelper.DecodeKeyword(keyword);
            var tagFilters = tmp.Item1;
            var keyWords = tmp.Item2;
            TagFilters = tagFilters;
            SetSelectedTabName(tagFilters);

            var newList = new List<ProtocolBaseViewModel>();
            if (Context?.AppData?.VmItemList == null) return;
            foreach (var vm in Context.AppData.VmItemList)
            {
                var server = vm.Server;
                var s = TagAndKeywordEncodeHelper.MatchKeywords(server, tagFilters, keyWords);
                if (s.Item1 == true)
                {
                    newList.Add(vm);
                }
            }

            ServerListItems = new ObservableCollection<ProtocolBaseViewModel>(newList);
            RaisePropertyChanged(nameof(IsSelectedAll));
            RaisePropertyChanged(nameof(IsMultipleSelected));
        }


        #region Commands

        private RelayCommand? _cmdAdd;
        public RelayCommand CmdAdd
        {
            get
            {
                return _cmdAdd ??= new RelayCommand((o) =>
                {
                    GlobalEventHelper.OnGoToServerAddPage?.Invoke(TagFilters.Where(x => x.IsIncluded == true).Select(x => x.TagName).ToList());
                });
            }
        }



        private RelayCommand? _cmdExportSelectedToJson;
        public RelayCommand CmdExportSelectedToJson
        {
            get
            {
                return _cmdExportSelectedToJson ??= new RelayCommand((o) =>
                {
                    if (Context?.DataService == null) return;
                    var path = SelectFileHelper.SaveFile(title: IoC.Get<ILanguageService>().Translate("system_options_data_security_export_dialog_title"),
                        filter: "PRM json array|*.prma",
                        selectedFileName: DateTime.Now.ToString("yyyyMMddhhmmss") + ".prma");
                    if (path == null) return;
                    var list = new List<ProtocolBase>();
                    foreach (var vs in ServerListItems.Where(x => (string.IsNullOrWhiteSpace(SelectedTabName) || x.Server.Tags?.Contains(SelectedTabName) == true) && x.IsSelected == true))
                    {
                        var serverBase = (ProtocolBase)vs.Server.Clone();
                        Context.DataService.DecryptToConnectLevel(ref serverBase);
                        list.Add(serverBase);
                    }
                    File.WriteAllText(path, JsonConvert.SerializeObject(list, Formatting.Indented), Encoding.UTF8);
                });
            }
        }



        private RelayCommand? _cmdImportFromJson;
        public RelayCommand CmdImportFromJson
        {
            get
            {
                return _cmdImportFromJson ??= new RelayCommand((o) =>
                {
                    var path = SelectFileHelper.OpenFile(title: IoC.Get<ILanguageService>().Translate("import_server_dialog_title"), filter: "PRM json array|*.prma");
                    if (path == null) return;
                    GlobalEventHelper.ShowProcessingRing?.Invoke(Visibility.Visible, IoC.Get<ILanguageService>().Translate("system_options_data_security_info_data_processing"));
                    Task.Factory.StartNew(() =>
                    {
                        try
                        {
                            var list = new List<ProtocolBase>();
                            var jobj = JsonConvert.DeserializeObject<List<object>>(File.ReadAllText(path, Encoding.UTF8)) ?? new List<object>();
                            foreach (var json in jobj)
                            {
                                var server = ItemCreateHelper.CreateFromJsonString(json.ToString()!);
                                if (server != null)
                                {
                                    server.Id = 0;
                                    list.Add(server);
                                }
                            }
                            if (Context?.AppData == null) return;
                            Context.AppData.AddServer(list);
                            GlobalEventHelper.ShowProcessingRing?.Invoke(Visibility.Collapsed, "");
                            Execute.OnUIThread(() =>
                            {
                                MessageBox.Show(IoC.Get<ILanguageService>().Translate("import_done_0_items_added").Replace("{0}", list.Count.ToString()), IoC.Get<ILanguageService>().Translate("messagebox_title_info"), MessageBoxButton.OK, MessageBoxImage.None, MessageBoxResult.None);
                            });
                        }
                        catch (Exception e)
                        {
                            SimpleLogHelper.Debug(e);
                            GlobalEventHelper.ShowProcessingRing?.Invoke(Visibility.Collapsed, "");
                            Execute.OnUIThread(() =>
                            {
                                MessageBox.Show(IoC.Get<ILanguageService>().Translate("import_failure_with_data_format_error"), IoC.Get<ILanguageService>().Translate("messagebox_title_error"), MessageBoxButton.OK, MessageBoxImage.Error, MessageBoxResult.None);
                            });
                        }
                    });
                });
            }
        }



        private RelayCommand? _cmdImportFromCsv;
        public RelayCommand CmdImportFromCsv
        {
            get
            {
                return _cmdImportFromCsv ??= new RelayCommand((o) =>
                {
                    var path = SelectFileHelper.OpenFile(title: IoC.Get<ILanguageService>().Translate("import_server_dialog_title"), filter: "csv|*.csv");
                    if (path == null) return;
                    GlobalEventHelper.ShowProcessingRing?.Invoke(Visibility.Visible, IoC.Get<ILanguageService>().Translate("system_options_data_security_info_data_processing"));
                    Task.Factory.StartNew(() =>
                    {
                        try
                        {
                            var list = MRemoteNgImporter.FromCsv(path, ServerIcons.Instance.Icons);
                            if (list?.Count > 0)
                            {
                                if (Context?.AppData == null) return;
                                Context.AppData.AddServer(list);
                                GlobalEventHelper.ShowProcessingRing?.Invoke(Visibility.Collapsed, "");
                                Execute.OnUIThread(() =>
                                {
                                    MessageBox.Show(IoC.Get<ILanguageService>().Translate("import_done_0_items_added").Replace("{0}", list.Count.ToString()), IoC.Get<ILanguageService>().Translate("messagebox_title_info"), MessageBoxButton.OK, MessageBoxImage.None, MessageBoxResult.None);
                                });
                                return;
                            }
                        }
                        catch (Exception e)
                        {
                            SimpleLogHelper.Debug(e);
                        }


                        GlobalEventHelper.ShowProcessingRing?.Invoke(Visibility.Collapsed, "");
                        Execute.OnUIThread(() =>
                        {
                            MessageBox.Show(IoC.Get<ILanguageService>().Translate("import_failure_with_data_format_error"), IoC.Get<ILanguageService>().Translate("messagebox_title_error"), MessageBoxButton.OK, MessageBoxImage.Error, MessageBoxResult.None);
                        });
                    });
                });
            }
        }



        private RelayCommand? _cmdDelete;
        public RelayCommand CmdDelete
        {
            get
            {
                return _cmdDelete ??= new RelayCommand((o) =>
                {
                    if (o is int id)
                    {
                        GlobalEventHelper.OnRequestDeleteServer?.Invoke(id);
                    }
                });
            }
        }

        private RelayCommand? _cmdDeleteSelected;
        public RelayCommand CmdDeleteSelected
        {
            get
            {
                return _cmdDeleteSelected ??= new RelayCommand((o) =>
                {
                    if (Context?.AppData == null) return;
                    var ss = ServerListItems.Where(x => x.IsSelected == true).ToList();
                    if (!(ss?.Count > 0)) return;
                    if (MessageBoxResult.Yes == MessageBox.Show(
                        IoC.Get<ILanguageService>().Translate("confirm_to_delete_selected"),
                        IoC.Get<ILanguageService>().Translate("messagebox_title_warning"), MessageBoxButton.YesNo,
                        MessageBoxImage.Question, MessageBoxResult.None))
                    {
                        var ids = ss.Select(x => x.Id);
                        Context.AppData.DeleteServer(ids);
                    }
                }, o => ServerListItems.Any(x => x.IsSelected == true));
            }
        }



        private RelayCommand? _cmdMultiEditSelected;
        public RelayCommand CmdMultiEditSelected
        {
            get
            {
                return _cmdMultiEditSelected ??= new RelayCommand((o) =>
                    {
                        GlobalEventHelper.OnRequestGoToServerMultipleEditPage?.Invoke(ServerListItems.Where(x => x.IsSelected).Select(x => x.Server), true);
                    }, o => ServerListItems.Any(x => x.IsSelected == true));
            }
        }



        private RelayCommand? _cmdCancelSelected;
        public RelayCommand CmdCancelSelected
        {
            get
            {
                Debug.Assert(Context != null);
                return _cmdCancelSelected ??= new RelayCommand((o) => { Context.AppData.UnselectAllServers(); });
            }
        }



        private DateTime _lastCmdReOrder;
        private RelayCommand? _cmdReOrder;
        public RelayCommand CmdReOrder
        {
            get
            {
                return _cmdReOrder ??= new RelayCommand((o) =>
                {
                    if (int.TryParse(o?.ToString() ?? "0", out int ot))
                    {
                        if ((DateTime.Now - _lastCmdReOrder).TotalMilliseconds > 200)
                        {
                            // cancel order
                            if (ServerOrderBy == (EnumServerOrderBy)(ot + 1))
                            {
                                ot = -1;
                            }
                            else if (ServerOrderBy == (EnumServerOrderBy)ot)
                            {
                                ++ot;
                            }

                            ServerOrderBy = (EnumServerOrderBy)ot;
                            ServerListItems = new ObservableCollection<ProtocolBaseViewModel>(GetOrderedVmProtocolServers(ServerListItems, ServerOrderBy));
                            _lastCmdReOrder = DateTime.Now;
                        }
                    }
                });
            }
        }



        private RelayCommand? _cmdConnectSelected;
        public RelayCommand CmdConnectSelected
        {
            get
            {
                return _cmdConnectSelected ??= new RelayCommand((o) =>
                {
                    var token = DateTime.Now.Ticks.ToString();
                    foreach (var vmProtocolServer in ServerListItems.Where(x => x.IsSelected == true).ToArray())
                    {
                        GlobalEventHelper.OnRequestServerConnect?.Invoke(vmProtocolServer.Id, token);
                        Thread.Sleep(50);
                    }
                });
            }
        }

        public void ShowTabByName(string tabName = "")
        {
            SelectedTabName = tabName;
        }
        #endregion
    }
}