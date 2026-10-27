using System.Diagnostics;
using System.Globalization;
using RuvioExamples.Desktop.Controls;
using RuvioExamples.LoadKit;

namespace RuvioExamples.Desktop;

internal sealed class MainForm : Form
{
    private const int PanelWidth = 312;

    private readonly LoadRunner _runner = new();
    private readonly LoadStats _stats = new();
    private readonly Stopwatch _clock = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 66 };
    private readonly CommandLine _cli = CommandLine.Parse(Environment.GetCommandLineArgs().Skip(1));
    private readonly ToolTip _tips = new();
    private readonly IReadOnlyList<ExampleManifest> _manifests;

    private readonly Panel _console = new() { Dock = DockStyle.Fill, BackColor = Theme.Bg, Visible = false };
    private readonly WizardView? _wizard;
    private readonly FlowLayoutPanel _presets = Ui.FlowPanel(PanelWidth);
    private readonly FlowLayoutPanel _endpoints = Ui.FlowPanel(PanelWidth);
    private readonly FlowLayoutPanel _traffic = Ui.FlowPanel(PanelWidth);
    private readonly Label _subtitle = Ui.Muted(string.Empty);
    private readonly CheckBox _sharedIdentity = Ui.PillCheck("one shared identity");
    private readonly TextBox _targets;
    private readonly SliderRow _clients, _rate, _work, _jitter, _stagger, _duration;
    private readonly Button _start, _reset;
    private readonly Label _notice;
    private readonly Tile _tSent, _tSuccess, _tRejected, _tFailed, _tRate, _tLatency;
    private readonly TimelineChart _chart = new() { Dock = DockStyle.Fill };
    private readonly LaneStrips _lanes = new() { Dock = DockStyle.Top, Height = 90 };
    private readonly InstanceBars _instances = new() { Dock = DockStyle.Top, Height = 62 };
    private readonly LogView _log = new() { Dock = DockStyle.Fill };

    private ExampleManifest _manifest;
    private CancellationTokenSource? _cts;
    private Task _runTask = Task.CompletedTask;
    private bool _running;

    public MainForm()
    {
        Text = "Ruvio Load Console";
        ClientSize = new Size(1500, 900);
        MinimumSize = new Size(1280, 780);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        DoubleBuffered = true;
        Padding = new Padding(18);

        _manifests = ExampleManifest.Discover(AppContext.BaseDirectory);

        if (_manifests.Count == 0)
        {
            _manifest = new ExampleManifest();
            Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Theme.Muted,
                Font = Theme.Title,
                Text = "No examples/*/console.json found next to this program.",
            });

            _targets = new TextBox();
            _clients = _rate = _work = _jitter = _stagger = _duration = new SliderRow();
            _start = _reset = new Button();
            _notice = new Label();
            _tSent = _tSuccess = _tRejected = _tFailed = _tRate = _tLatency = new Tile();
            return;
        }

        _manifest = _manifests.FirstOrDefault(m => m.Id.Contains(_cli.Example ?? string.Empty, StringComparison.OrdinalIgnoreCase)) ?? _manifests[0];

        // ── left: scenario panel ───────────────────────────────────────────────────────────────────────────────
        var body = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = Theme.Panel,
        };

        body.HandleCreated += (_, _) => Ui.DarkScrollbars(body);

        body.Controls.Add(new Label
        {
            Text = "●  Ruvio Load Console",
            Font = Theme.Title,
            ForeColor = Theme.Text,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 2),
        });
        body.Controls.Add(_subtitle);

        var guided = Ui.Flat("Ask me questions instead", Theme.Panel2, Theme.Text);
        guided.Margin = new Padding(0, 12, 0, 0);
        guided.Click += (_, _) => ShowWizard();
        _tips.SetToolTip(guided, "A short question-and-answer setup: instances, endpoint, clients, speed, duration.");
        body.Controls.Add(guided);

        body.Controls.Add(Ui.Caption("SCENARIOS"));
        body.Controls.Add(_presets);

        body.Controls.Add(Ui.Caption("ENDPOINT"));
        body.Controls.Add(_endpoints);

        body.Controls.Add(Ui.Caption("API INSTANCES  (comma separated, used in turn)"));
        _targets = new TextBox
        {
            BackColor = Theme.Panel2,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            Font = Theme.Mono,
            Width = PanelWidth,
        };
        body.Controls.Add(_targets);

        body.Controls.Add(Ui.Caption("TRAFFIC"));
        body.Controls.Add(_traffic);

        _tips.SetToolTip(_sharedIdentity, "All clients send the same identity header, so they share one budget, lock owner or counter.");
        body.Controls.Add(_sharedIdentity);

        body.Controls.Add(Ui.Spacer(8));
        body.Controls.Add(_clients = Ui.Slider("Clients", 1, 12, 1, 3, v => v.ToString("0"), PanelWidth));
        body.Controls.Add(_rate = Ui.Slider("Requests per second, per client", 0.5, 50, 0.5, 3, v => $"{v:0.#} req/s", PanelWidth));
        body.Controls.Add(_work = Ui.Slider("Fake work on the server", 0, 2000, 50, 100, v => $"{v:0} ms", PanelWidth));
        body.Controls.Add(_jitter = Ui.Slider("Jitter between requests", 0, 100, 5, 30, v => $"{v:0} %", PanelWidth));
        body.Controls.Add(_stagger = Ui.Slider("Fake wait between client starts", 0, 3000, 100, 0, v => $"{v:0} ms", PanelWidth));
        body.Controls.Add(_duration = Ui.Slider("Duration (0 = until stopped)", 0, 300, 5, 0, v => v == 0 ? "until stopped" : $"{v:0} s", PanelWidth));

        _start = Ui.Flat("Start", Theme.Accent, Theme.AccentInk);
        _start.Font = new Font("Segoe UI Semibold", 11f);
        _start.Dock = DockStyle.Top;
        _start.Height = 42;
        _start.Click += OnStartStop;

        _reset = Ui.Flat("Reset", Theme.Panel2, Theme.Text);
        var clear = Ui.Flat("Clear screen", Theme.Panel2, Theme.Text);
        _reset.Click += OnReset;
        clear.Click += (_, _) => { _stats.Reset(); Refresh(); };

        var secondary = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 40,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Theme.Panel,
            Padding = new Padding(0, 8, 0, 0),
        };
        secondary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        secondary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _reset.Dock = clear.Dock = DockStyle.Fill;
        _reset.Margin = new Padding(0, 0, 4, 0);
        clear.Margin = new Padding(4, 0, 0, 0);
        secondary.Controls.Add(_reset, 0, 0);
        secondary.Controls.Add(clear, 1, 0);

        _notice = new Label
        {
            Dock = DockStyle.Top,
            Height = 62,
            ForeColor = Theme.Muted,
            Font = Theme.Small,
            Padding = new Padding(0, 10, 0, 0),
        };

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 160, BackColor = Theme.Panel };
        bottom.Controls.Add(_notice);
        bottom.Controls.Add(secondary);
        bottom.Controls.Add(_start);

        var left = new Card { Dock = DockStyle.Fill };
        left.Controls.Add(body);
        left.Controls.Add(bottom);

        // ── right: live view ───────────────────────────────────────────────────────────────────────────────────
        var tiles = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 1, BackColor = Theme.Bg };

        for (int i = 0; i < 6; i++)
            tiles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 6));

        _tSent = Ui.NewTile("SENT", Theme.Text);
        _tSuccess = Ui.NewTile(string.Empty, Theme.Success);
        _tRejected = Ui.NewTile(string.Empty, Theme.Rejected);
        _tFailed = Ui.NewTile("503 / ERRORS", Theme.Warn);
        _tRate = Ui.NewTile("REQUESTS / S", Theme.Text);
        _tLatency = Ui.NewTile("LATENCY  p50 / p95", Theme.Text);

        var all = new[] { _tSent, _tSuccess, _tRejected, _tFailed, _tRate, _tLatency };

        for (int i = 0; i < all.Length; i++)
        {
            all[i].Margin = new Padding(0, 0, i == all.Length - 1 ? 0 : 10, 0);
            tiles.Controls.Add(all[i], i, 0);
        }

        var chartCard = new Card { Dock = DockStyle.Fill };
        chartCard.Controls.Add(_chart);

        var laneHeader = new Label
        {
            Dock = DockStyle.Top,
            Height = 26,
            Font = Theme.BodyBold,
            ForeColor = Theme.Text,
            BackColor = Theme.Panel,
            Text = "Clients   (each block is one request, newest on the right)",
        };

        var laneScroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Panel };
        laneScroll.Controls.Add(_lanes);

        var laneCard = new Card { Dock = DockStyle.Fill };
        laneCard.Controls.Add(laneScroll);
        laneCard.Controls.Add(laneHeader);

        var sideCard = new Card { Dock = DockStyle.Fill };
        sideCard.Controls.Add(_log);
        sideCard.Controls.Add(Ui.Spacer(10, DockStyle.Top));
        sideCard.Controls.Add(_instances);

        var lower = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = Theme.Bg };
        lower.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        lower.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 14));
        lower.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 340));
        lower.Controls.Add(laneCard, 0, 0);
        lower.Controls.Add(sideCard, 2, 0);

        var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, BackColor = Theme.Bg };
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 14));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 230));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 14));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        right.Controls.Add(tiles, 0, 0);
        right.Controls.Add(chartCard, 0, 2);
        right.Controls.Add(lower, 0, 4);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = Theme.Bg };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 380));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 18));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.Controls.Add(left, 0, 0);
        root.Controls.Add(right, 2, 0);
        _console.Controls.Add(root);

        // ── wizard ─────────────────────────────────────────────────────────────────────────────────────────────
        _wizard = new WizardView(_manifests, _manifest);
        _wizard.Completed += plan =>
        {
            if (_cli.Seconds is { } seconds)
                plan = plan with { DurationSeconds = seconds };

            ApplyPlan(plan);
            ShowConsole();
            StartRun();
        };
        _wizard.AdvancedRequested += ShowConsole;
        _wizard.ExampleChanged += manifest =>
        {
            _manifest = manifest;
            LoadManifest();
        };

        Controls.Add(_console);
        Controls.Add(_wizard);

        LoadManifest();

        _runner.ResultReceived += _stats.Add;
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();

        if (_cli.Scripted)
            ShowConsole();
        else
            ShowWizard();

        Shown += async (_, _) => await RunFromCommandLineAsync();
        FormClosed += (_, _) => { _cts?.Cancel(); _runner.Dispose(); };
    }

    // ── manifest and screens ───────────────────────────────────────────────────────────────────────────────────

    private void LoadManifest()
    {
        _subtitle.Text = _manifest.Title;
        _notice.Text = _manifest.Description.Length > 0
            ? $"{_manifest.Description} Pick a scenario and press Start."
            : "Pick a scenario and press Start.";

        _presets.Controls.Clear();

        foreach (var preset in _manifest.Presets)
        {
            var button = Ui.Flat(preset.Name, Theme.Panel2, Theme.Text);
            button.Margin = new Padding(0, 0, 6, 6);
            button.Click += (_, _) =>
            {
                ApplyPlan(preset.ToPlan(_manifest));
                _notice.Text = preset.Hint;
            };
            _tips.SetToolTip(button, preset.Hint);
            _presets.Controls.Add(button);
        }

        _endpoints.Controls.Clear();

        foreach (var endpoint in _manifest.Endpoints)
        {
            var pill = Ui.PillRadio(endpoint.Label, endpoint);
            _tips.SetToolTip(pill, endpoint.Expect);
            _endpoints.Controls.Add(pill);
        }

        _traffic.Controls.Clear();
        _traffic.Controls.Add(Ui.PillRadio("steady", "steady", isChecked: true));

        foreach (var burst in _manifest.Bursts)
        {
            var pill = Ui.PillRadio(burst.Label, burst);
            _tips.SetToolTip(pill, burst.Note);
            _traffic.Controls.Add(pill);
        }

        _targets.Text = _manifest.Targets[0];
        SelectEndpoint(_manifest.FindEndpoint("fixed")?.Id ?? _manifest.Endpoints[0].Id);

        string success = _manifest.Outcomes.Success.Label.ToUpperInvariant();
        string rejected = _manifest.Outcomes.Rejected.Label.ToUpperInvariant();
        string statuses = string.Join("/", _manifest.Outcomes.Rejected.Statuses);

        _tSuccess.Caption = $"{success}  2xx";
        _tRejected.Caption = statuses.Length > 0 ? $"{rejected}  {statuses}" : rejected;
        _chart.SuccessLabel = _manifest.Outcomes.Success.Label;
        _chart.RejectedLabel = $"{_manifest.Outcomes.Rejected.Label} ({statuses})";

        _reset.Visible = _manifest.Reset is not null;
        _reset.Text = _manifest.Reset?.Label ?? "Reset";

        if (_manifest.Reset is { Hint.Length: > 0 } reset)
            _tips.SetToolTip(_reset, reset.Hint);
    }

    private void ShowWizard()
    {
        _wizard!.Restart(_manifest);
        _console.Visible = false;
        _wizard.Visible = true;
        _wizard.BringToFront();
    }

    private void ShowConsole()
    {
        _wizard!.Visible = false;
        _console.Visible = true;
        _console.BringToFront();
    }

    // ── plan <-> controls ──────────────────────────────────────────────────────────────────────────────────────

    private void ApplyPlan(TestPlan plan)
    {
        SelectEndpoint(plan.EndpointId);

        _targets.Text = string.Join(", ", plan.Targets.Select(uri => uri.GetLeftPart(UriPartial.Authority)));
        _clients.Value = plan.Clients;
        _rate.Value = plan.Rate;
        _work.Value = plan.WorkMs;
        _jitter.Value = plan.JitterPercent;
        _stagger.Value = plan.StaggerMs;
        _duration.Value = plan.DurationSeconds;
        _sharedIdentity.Checked = plan.SharedIdentity;

        var burst = _manifest.FindBurst(plan.BurstId);
        var radio = _traffic.Controls.OfType<RadioButton>().FirstOrDefault(r => burst is not null ? ReferenceEquals(r.Tag, burst) : r.Tag is "steady");
        (radio ?? _traffic.Controls.OfType<RadioButton>().First()).Checked = true;
    }

    private TestPlan CurrentPlan()
    {
        var endpoint = _endpoints.Controls.OfType<RadioButton>().FirstOrDefault(r => r.Checked)?.Tag as EndpointDefinition
            ?? _manifest.Endpoints[0];
        var burst = _traffic.Controls.OfType<RadioButton>().FirstOrDefault(r => r.Checked)?.Tag as BurstDefinition;

        return new TestPlan
        {
            Targets = ParseTargets(),
            EndpointId = endpoint.Id,
            Clients = (int)_clients.Value,
            Rate = _rate.Value,
            JitterPercent = (int)_jitter.Value,
            WorkMs = (int)_work.Value,
            StaggerMs = (int)_stagger.Value,
            DurationSeconds = (int)_duration.Value,
            BurstId = burst?.Id,
            SharedIdentity = _sharedIdentity.Checked,
        };
    }

    // Checking one radio button unchecks its siblings; never uncheck by hand, that left no endpoint selected.
    private void SelectEndpoint(string idOrPath)
    {
        var endpoint = _manifest.FindEndpoint(idOrPath) ?? _manifest.Endpoints[0];
        var radio = _endpoints.Controls.OfType<RadioButton>().FirstOrDefault(r => ReferenceEquals(r.Tag, endpoint));

        if (radio is not null)
            radio.Checked = true;
    }

    // ── running ────────────────────────────────────────────────────────────────────────────────────────────────

    private void OnStartStop(object? sender, EventArgs e)
    {
        if (_running)
            _cts?.Cancel();
        else
            StartRun();
    }

    private void StartRun() => _runTask = StartAsync();

    private async Task StartAsync()
    {
        var plan = CurrentPlan();

        if (plan.Targets.Count == 0)
        {
            _notice.Text = "Enter at least one API address, for example http://localhost:5001";
            return;
        }

        var scenario = plan.ToScenario(_manifest);

        _stats.Reset();
        _cts = new CancellationTokenSource();
        _clock.Restart();
        SetRunning(true);

        try
        {
            await Task.Run(() => _runner.RunAsync(scenario, _cts.Token));
        }
        catch (Exception error)
        {
            _notice.Text = error.Message;
        }
        finally
        {
            _clock.Stop();
            SetRunning(false);
            Refresh();
        }
    }

    private void SetRunning(bool running)
    {
        _running = running;
        _start.Text = running ? "Stop" : "Start";
    }

    private List<Uri> ParseTargets() => [.. _targets.Text
        .Split([',', ';', ' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(text => Uri.TryCreate(text, UriKind.Absolute, out var uri) ? uri : null)
        .OfType<Uri>()];

    private async void OnReset(object? sender, EventArgs e)
    {
        if (_manifest.Reset is not { } reset)
            return;

        var targets = ParseTargets();
        int ok = 0;

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        foreach (var target in targets)
        {
            try
            {
                using var response = await http.PostAsync(new Uri(target, reset.Path), null);
                ok += response.IsSuccessStatusCode ? 1 : 0;
            }
            catch (Exception)
            {
                // The notice below tells the story.
            }
        }

        _notice.Text = ok == targets.Count && ok > 0
            ? $"{reset.Label}: done on {ok} instance(s). {reset.Hint}"
            : $"{reset.Label} reached {ok} of {targets.Count} instance(s). Is the API running?";
    }

    public override void Refresh()
    {
        var snapshot = _stats.Snapshot(_clock.Elapsed);

        _tSent.Value = Format(snapshot.Sent);
        _tSuccess.Value = Format(snapshot.Success);
        _tRejected.Value = Format(snapshot.Rejected);
        _tFailed.Value = Format(snapshot.Unavailable + snapshot.Errors);
        _tRate.Value = _running ? snapshot.RequestsPerSecond.ToString("0.#", CultureInfo.InvariantCulture) : "0";
        _tLatency.Value = snapshot.Sent == 0 ? "-" : $"{snapshot.P50Ms:0} / {snapshot.P95Ms:0} ms";

        _chart.Update(snapshot.Seconds);
        _lanes.Update(snapshot.Lanes);
        _instances.Update(snapshot.ByInstance);
        _log.Update(snapshot.Recent);
    }

    private static string Format(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private void InitializeComponent()
    {

    }

    // ── scripted runs ──────────────────────────────────────────────────────────────────────────────────────────
    //   RuvioLoadConsole.exe --preset 2 --autostart --seconds 20 --snapshot out/fixed.png    (console, scenario 2)
    //   RuvioLoadConsole.exe --step 3 --snapshot out/step3.png                                (screenshot of wizard step 3)
    //   RuvioLoadConsole.exe --wizard-auto --seconds 8 --snapshot out/wizard-run.png          (accept every default, run)

    private async Task RunFromCommandLineAsync()
    {
        if (_manifests.Count == 0)
            return;

        if (_cli.Preset is { } number && number >= 1 && number <= _manifest.Presets.Count)
        {
            ApplyPlan(_manifest.Presets[number - 1].ToPlan(_manifest));
            _notice.Text = _manifest.Presets[number - 1].Hint;
        }

        if (_cli.Endpoint is { } endpoint)
            SelectEndpoint(endpoint);

        if (_cli.Targets is { } targets)
            _targets.Text = targets;

        if (_cli.Step is { } step)
            _wizard!.Go(step - 1);

        if (_cli.WizardAuto)
        {
            for (int guard = 0; guard < 20 && _wizard!.Visible; guard++)
                _wizard.Next();
        }

        if (_cli.AutoStart)
        {
            if (_cli.Seconds is { } seconds)
                _duration.Value = seconds;

            if (!_running)
                StartRun();
        }

        await _runTask;
        await Task.Delay(400);

        if (_cli.Snapshot is { } file)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);

            using var bitmap = new Bitmap(ClientSize.Width, ClientSize.Height);
            DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(file, System.Drawing.Imaging.ImageFormat.Png);

            Close();
        }
    }

    private sealed record CommandLine(
        string? Example, int? Preset, string? Endpoint, string? Targets, bool AutoStart, int? Seconds, string? Snapshot, int? Step, bool WizardAuto)
    {
        public bool Scripted => Preset is not null || Endpoint is not null || Targets is not null || AutoStart;

        public static CommandLine Parse(IEnumerable<string> args)
        {
            var list = args.ToList();

            string? Value(string name)
            {
                int index = list.IndexOf(name);
                return index >= 0 && index + 1 < list.Count ? list[index + 1] : null;
            }

            return new CommandLine(
                Value("--example"),
                int.TryParse(Value("--preset"), out int preset) ? preset : null,
                Value("--endpoint"),
                Value("--targets"),
                list.Contains("--autostart"),
                int.TryParse(Value("--seconds"), out int seconds) ? seconds : null,
                Value("--snapshot"),
                int.TryParse(Value("--step"), out int step) ? step : null,
                list.Contains("--wizard-auto"));
        }
    }
}
