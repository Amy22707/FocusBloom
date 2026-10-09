using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using FocusBloom.Core.Restrictions;
using FocusBloom.Core.Storage;
using FocusBloom.Desktop.Presentation;

namespace FocusBloom.Desktop.ViewModels;

public sealed record RestrictionChoice(bool Strict, string Name);
public sealed record AccessListChoice(AccessListMode Mode, string Name);

public sealed class RestrictionSettingsViewModel : ObservableObject
{
    private readonly IFocusStore _store;
    private readonly Action<string> _report;
    private string _domain = "";
    private bool _subdomains = true;
    private WebsiteRule? _selectedWebsite;
    private AllowedApplication? _selectedApplication;
    private readonly ObservableCollection<AllowedApplication> _whiteApplications;
    private readonly ObservableCollection<AllowedApplication> _blackApplications;
    private readonly ObservableCollection<WebsiteRule> _whiteWebsites;
    private readonly ObservableCollection<WebsiteRule> _blackWebsites;
    private AccessListChoice _applicationMode;
    private AccessListChoice _websiteMode;

    public RestrictionSettingsViewModel(IFocusStore store, Action<string> report)
    {
        _store = store;
        _report = report;
        _whiteApplications = new(Read<AllowedApplication>("restrictions.applications"));
        _blackApplications = new(Read<AllowedApplication>("restrictions.applications.blacklist"));
        _whiteWebsites = new(Read<WebsiteRule>("restrictions.websites"));
        _blackWebsites = new(Read<WebsiteRule>("restrictions.websites.blacklist"));
        _applicationMode = ListModes.First(m => m.Mode.ToString() == (store.GetSetting("restrictions.applicationMode") ?? "Whitelist"));
        _websiteMode = ListModes.First(m => m.Mode.ToString() == (store.GetSetting("restrictions.websiteMode") ?? "Whitelist"));
        _whiteWebsites.CollectionChanged += (_, _) => Notify(nameof(Websites));
        _blackWebsites.CollectionChanged += (_, _) => Notify(nameof(Websites));
        SaveWebsiteCommand = new RelayCommand(SaveWebsite);
        NewWebsiteCommand = new RelayCommand(() => { SelectedWebsite = null; Domain = ""; IncludeSubdomains = true; });
        RemoveWebsiteCommand = new RelayCommand(() =>
        {
            if (SelectedWebsite is null) return;
            var error = DeleteWebsite(SelectedWebsite.Domain);
            if (error is not null) { _report(error); return; }
            SelectedWebsite = null;
        });
        RemoveApplicationCommand = new RelayCommand(() =>
        {
            if (SelectedApplication is null) return;
            Applications.Remove(SelectedApplication);
            SelectedApplication = null;
            Save(ApplicationSettingKey, Applications);
        });
    }

    public IReadOnlyList<AccessListChoice> ListModes { get; } = new[]
    {
        new AccessListChoice(AccessListMode.Whitelist, "白名单 · 仅允许列表内"),
        new AccessListChoice(AccessListMode.Blacklist, "黑名单 · 仅限制列表内")
    };
    public AccessListChoice ApplicationMode
    {
        get => _applicationMode;
        set
        {
            if (value == _applicationMode) return;
            if (!SaveMode("restrictions.applicationMode", value.Mode)) return;
            _applicationMode = value; SelectedApplication = null; Notify(); Notify(nameof(Applications)); Notify(nameof(ApplicationListTitle));
        }
    }
    public AccessListChoice WebsiteMode
    {
        get => _websiteMode;
        set
        {
            if (value == _websiteMode) return;
            if (!SaveMode("restrictions.websiteMode", value.Mode)) return;
            _websiteMode = value; SelectedWebsite = null; Domain = ""; Notify(); Notify(nameof(Websites)); Notify(nameof(WebsiteListTitle));
        }
    }
    public string ApplicationListTitle => ApplicationMode.Mode == AccessListMode.Whitelist ? "允许操作的应用" : "限制操作的应用";
    public string WebsiteListTitle => WebsiteMode.Mode == AccessListMode.Whitelist ? "允许访问的网站" : "限制访问的网站";
    public ObservableCollection<AllowedApplication> Applications => ApplicationMode.Mode == AccessListMode.Whitelist ? _whiteApplications : _blackApplications;
    public ObservableCollection<WebsiteRule> Websites => WebsiteMode.Mode == AccessListMode.Whitelist ? _whiteWebsites : _blackWebsites;
    private string ApplicationSettingKey => ApplicationMode.Mode == AccessListMode.Whitelist ? "restrictions.applications" : "restrictions.applications.blacklist";
    private string WebsiteSettingKey => WebsiteMode.Mode == AccessListMode.Whitelist ? "restrictions.websites" : "restrictions.websites.blacklist";
    public string Domain { get => _domain; set { _domain = value; Notify(); } }
    public bool IncludeSubdomains { get => _subdomains; set { _subdomains = value; Notify(); } }
    public WebsiteRule? SelectedWebsite
    {
        get => _selectedWebsite;
        set { _selectedWebsite = value; Notify(); if (value is not null) { Domain = value.Domain; IncludeSubdomains = value.IncludeSubdomains; } }
    }
    public AllowedApplication? SelectedApplication { get => _selectedApplication; set { _selectedApplication = value; Notify(); } }
    public RelayCommand SaveWebsiteCommand { get; }
    public RelayCommand NewWebsiteCommand { get; }
    public RelayCommand RemoveWebsiteCommand { get; }
    public RelayCommand RemoveApplicationCommand { get; }

    public void AddApplication(AllowedApplication application)
    {
        if (Applications.Any(a => a.ExecutablePath.Equals(application.ExecutablePath, StringComparison.OrdinalIgnoreCase))) return;
        Applications.Add(application);
        SelectedApplication = application;
        Save(ApplicationSettingKey, Applications);
    }

    private void SaveWebsite()
    {
        var error = UpsertWebsite(Domain, IncludeSubdomains, SelectedWebsite?.Domain);
        if (error is not null) _report(error);
    }

    public string? UpsertWebsite(string domain, bool includeSubdomains, string? originalDomain = null)
    {
        if (!WebsiteRule.TryCreate(domain, includeSubdomains, out var rule)) return "请输入有效域名或 http(s) 网址。";
        var rules = Websites.Where(w => w.Domain != originalDomain && w.Domain != rule!.Domain).Append(rule!).ToArray();
        var error = SaveWebsites(rules);
        if (error is null) SelectedWebsite = rule;
        return error;
    }

    public string? DeleteWebsite(string domain) => SaveWebsites(Websites.Where(w => w.Domain != domain).ToArray());

    private string? SaveWebsites(IReadOnlyList<WebsiteRule> rules)
    {
        try
        {
            _store.SaveSetting(WebsiteSettingKey, JsonSerializer.Serialize(rules));
            Websites.Clear();
            foreach (var rule in rules) Websites.Add(rule);
            _report("网站列表已保存，下一次严格专注生效。");
            return null;
        }
        catch (Exception ex) { return $"网站列表保存失败：{ex.Message}"; }
    }

    private IReadOnlyList<T> Read<T>(string key) => _store.GetSetting(key) is { } json
        ? JsonSerializer.Deserialize<List<T>>(json)! : Array.Empty<T>();

    private void Save<T>(string key, IEnumerable<T> values)
    {
        try { _store.SaveSetting(key, JsonSerializer.Serialize(values)); _report("应用列表已保存，下一次严格专注生效。"); }
        catch (Exception ex) { _report($"应用列表保存失败：{ex.Message}"); }
    }

    private bool SaveMode(string key, AccessListMode mode)
    {
        try { _store.SaveSetting(key, mode.ToString()); _report("限制方式已保存，下一次严格专注生效。"); return true; }
        catch (Exception ex) { _report($"限制方式保存失败：{ex.Message}"); return false; }
    }
}
