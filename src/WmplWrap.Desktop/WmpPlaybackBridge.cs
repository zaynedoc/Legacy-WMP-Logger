using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WmplWrap.Desktop;

/// <summary>
/// Hosts a hidden, remoted WMP ActiveX control on its own STA thread. The control shares
/// the full player playback engine and is used only to read the current media and state.
/// </summary>
internal sealed class WmpPlaybackBridge : IDisposable
{
    private readonly object _gate = new();
    private Thread? _thread;
    private Form? _form;
    private RemoteWmpControl? _control;
    private TaskCompletionSource<bool>? _ready;
    private bool _disposed;

    public WmpPlaybackSnapshot? LastPlayback { get; private set; }
    public string LastError { get; private set; } = "";
    public string ConnectionState { get; private set; } = "Not connected";

    public async Task<WmpPlaybackSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        if (!IsWmpRunning())
        {
            LastPlayback = WmpPlaybackSnapshot.None;
            LastError = "";
            ConnectionState = "Windows Media Player is not running";
            Disconnect();
            return LastPlayback;
        }

        try
        {
            await EnsureStartedAsync(cancellationToken);
            var playback = await InvokeAsync(() => _control?.ReadPlayback() ?? WmpPlaybackSnapshot.None, cancellationToken);
            LastPlayback = playback;
            LastError = _control?.LastError ?? "";
            ConnectionState = _control?.ConnectionState ?? "Reader control is unavailable";
            return playback;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error)
        {
            LastPlayback = WmpPlaybackSnapshot.None;
            LastError = Describe(error);
            ConnectionState = "Could not start the Windows Media Player reader";
            return LastPlayback;
        }
    }

    public void Disconnect()
    {
        Form? form;
        Thread? thread;
        lock (_gate)
        {
            form = _form;
            thread = _thread;
        }

        if (form?.IsHandleCreated == true && !form.IsDisposed)
        {
            try { form.BeginInvoke(new Action(form.Close)); }
            catch (InvalidOperationException) { }
        }

        if (thread is not null && thread != Thread.CurrentThread)
            thread.Join(TimeSpan.FromSeconds(1));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Disconnect();
        GC.SuppressFinalize(this);
    }

    private async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        Task ready;
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_thread is { IsAlive: true } && _ready is not null)
            {
                ready = _ready.Task;
            }
            else
            {
                _ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _thread = new Thread(RunMessageLoop)
                {
                    IsBackground = true,
                    Name = "WMPL Wrap WMP playback reader"
                };
                _thread.SetApartmentState(ApartmentState.STA);
                _thread.Start();
                ready = _ready.Task;
            }
        }

        await ready.WaitAsync(cancellationToken);
    }

    private void RunMessageLoop()
    {
        try
        {
            using var form = new Form
            {
                ClientSize = new Size(1, 1),
                FormBorderStyle = FormBorderStyle.None,
                Location = new Point(-32000, -32000),
                Opacity = 0,
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual,
                Text = "WMPL Wrap playback reader"
            };
            using var control = new RemoteWmpControl { Dock = DockStyle.Fill };
            form.Controls.Add(control);
            form.Load += (_, _) => _ready?.TrySetResult(true);

            lock (_gate)
            {
                _form = form;
                _control = control;
            }

            Application.Run(form);
        }
        catch (Exception error)
        {
            LastError = Describe(error);
            ConnectionState = "Could not create the Windows Media Player reader";
            _ready?.TrySetException(error);
        }
        finally
        {
            lock (_gate)
            {
                _form = null;
                _control = null;
                _thread = null;
            }
        }
    }

    private Task<T> InvokeAsync<T>(Func<T> work, CancellationToken cancellationToken)
    {
        Form? form;
        lock (_gate) form = _form;
        if (form?.IsHandleCreated != true || form.IsDisposed)
            return Task.FromResult(work());

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            form.BeginInvoke(new Action(() =>
            {
                try { completion.TrySetResult(work()); }
                catch (Exception error) { completion.TrySetException(error); }
            }));
        }
        catch (InvalidOperationException error)
        {
            completion.TrySetException(error);
        }

        return completion.Task.WaitAsync(cancellationToken);
    }

    private static bool IsWmpRunning()
    {
        try { return Process.GetProcessesByName("wmplayer").Length > 0; }
        catch { return false; }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(WmpPlaybackBridge));
    }

    private static string Describe(Exception error) =>
        string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : $"{error.GetType().Name}: {error.Message}";
}

[ComVisible(true)]
[ClassInterface(ClassInterfaceType.AutoDispatch)]
public sealed class RemoteWmpControl : AxHost, IOleClientSite, IOleServiceProvider
{
    private const string WmpControlClassId = "6BF52A52-394A-11D3-B153-00C04F79FAA6";
    private readonly WmpRemoteMediaServices _remoteServices = new();

    public string LastError { get; private set; } = "";
    public string ConnectionState { get; private set; } = "Reader is initializing";
    public bool RemoteServicesWereRequested { get; private set; }

    public RemoteWmpControl() : base(WmpControlClassId) { }

    protected override void AttachInterfaces()
    {
        base.AttachInterfaces();
        if (GetOcx() is IOleObject oleObject)
        {
            oleObject.SetClientSite(this);
            ConnectionState = "Remote client site attached";
        }
        else
        {
            ConnectionState = "The WMP ActiveX control did not expose its OLE client site";
        }
    }

    public WmpPlaybackSnapshot ReadPlayback()
    {
        dynamic? player = null;
        dynamic? media = null;
        try
        {
            LastError = "";
            player = GetOcx();
            if (player is null)
            {
                ConnectionState = "The WMP ActiveX control is not available";
                return WmpPlaybackSnapshot.None;
            }

            var rawState = Convert.ToInt32(player.playState, CultureInfo.InvariantCulture);
            var isRemote = Value(() => player.isRemote);
            var state = ToPlaybackState(rawState);
            ConnectionState = $"WMP control connected (remote service requested: {RemoteServicesWereRequested}, remote: {isRemote}, play state: {rawState})";
            if (state == WmpPlaybackState.None) return WmpPlaybackSnapshot.None;

            media = player.currentMedia;
            if (media is null)
            {
                ConnectionState = "WMP reported active playback without current media";
                return WmpPlaybackSnapshot.None;
            }

            var title = Attribute(media, "Title");
            var artist = FirstValue(Attribute(media, "Author"), Attribute(media, "WM/Artist"), Attribute(media, "WM/AlbumArtist"));
            var albumArtist = FirstValue(Attribute(media, "WM/AlbumArtist"), artist);
            var album = Attribute(media, "WM/AlbumTitle");
            var sourceUrl = FirstValue(Value(() => media.sourceURL), Value(() => player.controls.currentItem.sourceURL));
            var duration = Value(() => media.durationString);
            var position = Number(() => player.controls.currentPosition);

            return string.IsNullOrWhiteSpace(title)
                ? WmpPlaybackSnapshot.None
                : new WmpPlaybackSnapshot(title, artist, album, albumArtist, sourceUrl, duration, position, state);
        }
        catch (Exception error)
        {
            LastError = string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : $"{error.GetType().Name}: {error.Message}";
            ConnectionState = "Reading the WMP ActiveX control failed";
            return WmpPlaybackSnapshot.None;
        }
        finally
        {
            if (media is not null && Marshal.IsComObject(media)) Marshal.FinalReleaseComObject(media);
        }
    }

    int IOleServiceProvider.QueryService(ref Guid serviceGuid, ref Guid interfaceGuid, out IntPtr interfacePointer)
    {
        interfacePointer = IntPtr.Zero;
        // WMP supplies the interface ID it needs here. The service ID is an implementation
        // detail of the control and differs between WMP versions, so only the requested
        // IWMPRemoteMediaServices interface should be used to accept the request.
        if (interfaceGuid != typeof(IWmpRemoteMediaServices).GUID)
            return unchecked((int)0x80004002);

        RemoteServicesWereRequested = true;
        interfacePointer = Marshal.GetComInterfaceForObject(_remoteServices, typeof(IWmpRemoteMediaServices));
        return 0;
    }

    int IOleClientSite.SaveObject() => 0;
    int IOleClientSite.GetMoniker(uint assign, uint which, out object? moniker) { moniker = null; return unchecked((int)0x80004001); }
    int IOleClientSite.GetContainer(out object? container) { container = null; return unchecked((int)0x80004001); }
    int IOleClientSite.ShowObject() => 0;
    int IOleClientSite.OnShowWindow(bool show) => 0;
    int IOleClientSite.RequestNewObjectLayout() => unchecked((int)0x80004001);

    private static WmpPlaybackState ToPlaybackState(int value) => value switch
    {
        2 => WmpPlaybackState.Paused,
        3 => WmpPlaybackState.Playing,
        _ => WmpPlaybackState.None
    };

    private static string Attribute(dynamic media, string name) => Value(() => media.getItemInfo(name));
    private static string Value(Func<object?> read)
    {
        try { return Convert.ToString(read(), CultureInfo.InvariantCulture)?.Trim() ?? ""; }
        catch { return ""; }
    }
    private static double Number(Func<object?> read) => double.TryParse(Value(read), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;
    private static string FirstValue(params string[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
}

[ComVisible(true)]
[Guid("CBB92747-741F-44FE-AB5B-F1A48F3B2A59")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IWmpRemoteMediaServices
{
    [return: MarshalAs(UnmanagedType.BStr)] string GetServiceType();
    [return: MarshalAs(UnmanagedType.BStr)] string GetApplicationName();
    [PreserveSig] int GetScriptableObject(
        [MarshalAs(UnmanagedType.BStr)] out string? name,
        [MarshalAs(UnmanagedType.IDispatch)] out object? dispatch);
    [PreserveSig] int GetCustomUIMode([MarshalAs(UnmanagedType.BStr)] out string? file);
}

[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
internal sealed class WmpRemoteMediaServices : IWmpRemoteMediaServices
{
    public string GetServiceType() => "Remote";
    public string GetApplicationName() => "WMPL Wrap";
    public int GetScriptableObject(out string? name, out object? dispatch)
    {
        name = null;
        dispatch = null;
        return unchecked((int)0x80004001);
    }

    public int GetCustomUIMode(out string? file)
    {
        file = null;
        return unchecked((int)0x80004001);
    }
}

[ComVisible(true)]
[Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IOleServiceProvider
{
    [PreserveSig] int QueryService(ref Guid serviceGuid, ref Guid interfaceGuid, out IntPtr interfacePointer);
}

[ComVisible(true)]
[Guid("00000118-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IOleClientSite
{
    [PreserveSig] int SaveObject();
    [PreserveSig] int GetMoniker(uint assign, uint which, [MarshalAs(UnmanagedType.Interface)] out object? moniker);
    [PreserveSig] int GetContainer([MarshalAs(UnmanagedType.Interface)] out object? container);
    [PreserveSig] int ShowObject();
    [PreserveSig] int OnShowWindow([MarshalAs(UnmanagedType.Bool)] bool show);
    [PreserveSig] int RequestNewObjectLayout();
}

[ComImport]
[Guid("00000112-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IOleObject
{
    void SetClientSite([MarshalAs(UnmanagedType.Interface)] IOleClientSite site);
}
