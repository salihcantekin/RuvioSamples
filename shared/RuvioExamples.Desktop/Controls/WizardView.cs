using System.Globalization;
using RuvioExamples.LoadKit;

namespace RuvioExamples.Desktop.Controls;

/// <summary>
/// Asks one question per screen (where, which endpoint, how many clients, how fast, how long) and ends with a plain
/// sentence describing the test. The answers become a <see cref="TestPlan"/>.
/// </summary>
internal sealed class WizardView : Panel
{
    private const int CardWidth = 860, CardHeight = 620, Inner = 788, Pad = 36;

    private readonly IReadOnlyList<ExampleManifest> _manifests;
    private readonly Card _card = new() { Width = CardWidth, Height = CardHeight };
    private readonly Label _stepLabel, _title, _hint;
    private readonly StepDots _dots = new();
    private readonly Panel _content = new() { BackColor = Theme.Panel };
    private readonly Button _back, _next, _advanced;

    private ExampleManifest _manifest;
    private List<Step> _steps = [];
    private int _index;

    private readonly HashSet<string> _targets = [];
    private string _endpointId = string.Empty;
    private string? _burstId;
    private int _clients = 3;
    private double _rate = 3;
    private int _duration = 30;
    private int _work = 100;
    private bool _shared;

    public WizardView(IReadOnlyList<ExampleManifest> manifests, ExampleManifest selected)
    {
        _manifests = manifests;
        _manifest = selected;

        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;

        _stepLabel = new Label { Font = Theme.SmallBold, ForeColor = Theme.Accent, BackColor = Theme.Panel, AutoSize = true, Location = new Point(Pad, 30) };
        _title = new Label { Font = Theme.Heading, ForeColor = Theme.Text, BackColor = Theme.Panel, Location = new Point(Pad, 84), Size = new Size(Inner, 40) };
        _hint = new Label { Font = Theme.Body, ForeColor = Theme.Muted, BackColor = Theme.Panel, Location = new Point(Pad, 132), Size = new Size(Inner, 48) };

        _dots.Location = new Point(CardWidth - Pad - 320, 28);
        _dots.Size = new Size(320, 18);

        _content.Location = new Point(Pad, 196);
        _content.Size = new Size(Inner, 320);

        _back = Ui.Flat("Back", Theme.Panel2, Theme.Text);
        _next = Ui.Flat("Next", Theme.Accent, Theme.AccentInk);
        _advanced = Ui.Flat("Skip the questions", Theme.Panel, Theme.Muted);
        _back.Location = new Point(Pad, CardHeight - 72);
        _advanced.Location = new Point(Pad + 90, CardHeight - 72);
        _next.Font = new Font("Segoe UI Semibold", 11f);
        _next.Size = new Size(170, 42);
        _next.Location = new Point(CardWidth - Pad - 170, CardHeight - 78);
        _back.Click += (_, _) => Go(_index - 1);
        _next.Click += (_, _) => Next();
        _advanced.Click += (_, _) => AdvancedRequested?.Invoke();

        _card.Controls.AddRange([_stepLabel, _dots, _title, _hint, _content, _back, _advanced, _next]);
        Controls.Add(_card);
        Resize += (_, _) => Center();

        ResetAnswers();
        Rebuild(0);
    }

    public event Action<TestPlan>? Completed;

    public event Action? AdvancedRequested;

    public event Action<ExampleManifest>? ExampleChanged;

    public ExampleManifest Manifest => _manifest;

    public int StepCount => _steps.Count;

    public void Restart(ExampleManifest manifest)
    {
        _manifest = manifest;
        ResetAnswers();
        Rebuild(0);
    }

    public void Go(int index) => Rebuild(index);

    public void Next()
    {
        if (_index >= _steps.Count - 1)
            Completed?.Invoke(CurrentPlan());
        else
            Rebuild(_index + 1);
    }

    public TestPlan CurrentPlan() => new()
    {
        Targets = [.. _manifest.Targets.Where(_targets.Contains).Select(address => new Uri(address))],
        EndpointId = _endpointId,
        Clients = _clients,
        Rate = _rate,
        WorkMs = _work,
        DurationSeconds = _duration,
        BurstId = _burstId,
        SharedIdentity = _shared,
    };

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        Center();
    }

    private void Center()
    {
        _card.Location = new Point(Math.Max(0, (Width - CardWidth) / 2), Math.Max(0, (Height - CardHeight) / 2));
    }

    private void ResetAnswers()
    {
        _targets.Clear();
        _targets.Add(_manifest.Targets[0]);
        _endpointId = (_manifest.FindEndpoint("fixed") ?? _manifest.Endpoints[0]).Id;
        _burstId = null;
        _clients = 3;
        _rate = 3;
        _duration = 30;
        _work = 100;
        _shared = false;
    }

    private void Rebuild(int index)
    {
        _steps = AllSteps().Where(step => step.IsActive()).ToList();
        _index = Math.Clamp(index, 0, _steps.Count - 1);

        var step = _steps[_index];
        bool last = _index == _steps.Count - 1;

        _stepLabel.Text = $"STEP {_index + 1} OF {_steps.Count}  ·  {_manifest.Title.ToUpperInvariant()}";
        _dots.Update(_steps.Count, _index);
        _title.Text = step.Title;
        _hint.Text = step.Hint;
        _back.Enabled = _index > 0;
        _next.Text = last ? "Start the test" : "Next";

        _content.SuspendLayout();
        _content.Controls.Clear();
        step.Build(_content);
        _content.ResumeLayout();

        UpdateNext();
    }

    private void UpdateNext()
    {
        _next.Enabled = _steps[_index].IsValid();
    }

    private IEnumerable<Step> AllSteps()
    {
        yield return new Step(
            "Which example do you want to test?",
            "Every example folder ships its own endpoints and scenarios.",
            BuildExample, () => _manifests.Count > 1, () => true);

        yield return new Step(
            "Which API instances should receive the requests?",
            "Pick two to send the same clients through both instances and see whether they share one result.",
            BuildTargets, () => true, () => _targets.Count > 0);

        yield return new Step(
            "Which endpoint should they call?",
            "Each endpoint protects the same fragile backend in a different way.",
            BuildEndpoints, () => true, () => true);

        yield return new Step(
            "What should the traffic look like?",
            "A steady stream is the everyday case. The patterns fire bursts at exact moments.",
            BuildShape, () => _manifest.Bursts.Count > 0, () => true);

        yield return new Step(
            "How many virtual clients?",
            "Think of each client as a different user. Each one sends its own identity header, so the API can tell them apart.",
            BuildClients, () => true, () => true);

        yield return new Step(
            "How many requests per second does each client send?",
            "The total is clients times this number. A little random jitter keeps it from looking like a metronome.",
            BuildRate, () => _burstId is null, () => true);

        yield return new Step(
            "How long should the test run?",
            "It stops by itself, or you can press Stop on the next screen at any time.",
            BuildDuration, () => true, () => true);

        yield return new Step(
            "Should the server pretend to be slow?",
            "Fake work makes every request take this long on the server, so slow requests can pile up.",
            BuildWork, () => true, () => true);

        yield return new Step(
            "Ready. This is what will happen.",
            "Press the button and the console starts sending requests. Every request shows up as a block on the screen.",
            BuildSummary, () => true, () => true);
    }

    private void BuildExample(Panel host)
    {
        var flow = Ui.FlowPanel(Inner);

        foreach (var manifest in _manifests)
        {
            var pill = Ui.PillRadio(manifest.Title, manifest, manifest == _manifest);
            pill.CheckedChanged += (_, _) =>
            {
                if (!pill.Checked || manifest == _manifest)
                    return;

                _manifest = manifest;
                ResetAnswers();
                ExampleChanged?.Invoke(manifest);
                Rebuild(_index);
            };
            flow.Controls.Add(pill);
        }

        host.Controls.Add(flow);
        host.Controls.Add(Note(_manifest.Description, 60));
    }

    private void BuildTargets(Panel host)
    {
        var flow = Ui.FlowPanel(Inner);

        foreach (string address in _manifest.Targets)
        {
            var uri = new Uri(address);
            var pill = Ui.PillCheck($"{uri.Host}:{uri.Port}", _targets.Contains(address));
            pill.CheckedChanged += (_, _) =>
            {
                if (pill.Checked)
                    _targets.Add(address);
                else
                    _targets.Remove(address);

                UpdateNext();
            };
            flow.Controls.Add(pill);
        }

        host.Controls.Add(flow);
        host.Controls.Add(Note("Start the instances with .\\scripts\\api.ps1 (it starts two). Requests are handed to the chosen instances in turn.", 48, top: 70));
    }

    private void BuildEndpoints(Panel host)
    {
        var note = Note(string.Empty, 90, top: 120);
        var flow = Ui.FlowPanel(Inner);

        foreach (var endpoint in _manifest.Endpoints)
        {
            var pill = Ui.PillRadio(endpoint.Label, endpoint, endpoint.Id == _endpointId);
            pill.CheckedChanged += (_, _) =>
            {
                if (!pill.Checked)
                    return;

                _endpointId = endpoint.Id;
                note.Text = endpoint.Expect;
            };
            flow.Controls.Add(pill);
        }

        note.Text = (_manifest.FindEndpoint(_endpointId) ?? _manifest.Endpoints[0]).Expect;
        host.Controls.Add(flow);
        host.Controls.Add(note);
    }

    private void BuildShape(Panel host)
    {
        var note = Note(string.Empty, 90, top: 70);
        var flow = Ui.FlowPanel(Inner);

        var steady = Ui.PillRadio("steady stream", "steady", _burstId is null);
        steady.CheckedChanged += (_, _) =>
        {
            if (!steady.Checked)
                return;

            _burstId = null;
            note.Text = "Every client sends at its own steady pace, with a little random jitter.";
        };
        flow.Controls.Add(steady);

        foreach (var burst in _manifest.Bursts)
        {
            var pill = Ui.PillRadio(burst.Label, burst, burst.Id == _burstId);
            pill.CheckedChanged += (_, _) =>
            {
                if (!pill.Checked)
                    return;

                _burstId = burst.Id;
                note.Text = burst.Note;
            };
            flow.Controls.Add(pill);
        }

        note.Text = _manifest.FindBurst(_burstId)?.Note ?? "Every client sends at its own steady pace, with a little random jitter.";
        host.Controls.Add(flow);
        host.Controls.Add(note);
    }

    private void BuildClients(Panel host)
    {
        var value = BigValue(host, $"{_clients}");
        var slider = Ui.Slider(string.Empty, 1, 12, 1, _clients, _ => string.Empty, Inner);
        slider.Location = new Point(0, 70);
        slider.ValueChanged += (_, _) =>
        {
            _clients = (int)slider.Value;
            value.Text = _clients.ToString(CultureInfo.InvariantCulture);
        };

        var shared = Ui.PillCheck("all clients share one identity", _shared);
        shared.Location = new Point(0, 140);
        shared.CheckedChanged += (_, _) => _shared = shared.Checked;

        host.Controls.Add(slider);
        host.Controls.Add(shared);
        host.Controls.Add(Note("Sharing one identity means one budget, one lock owner or one counter for everybody, depending on the example.", 48, top: 190));
    }

    private void BuildRate(Panel host)
    {
        var value = BigValue(host, RateText());
        var slider = Ui.Slider(string.Empty, 0.5, 50, 0.5, _rate, _ => string.Empty, Inner);
        slider.Location = new Point(0, 70);
        slider.ValueChanged += (_, _) =>
        {
            _rate = slider.Value;
            value.Text = RateText();
        };

        host.Controls.Add(slider);
    }

    private string RateText() => string.Create(
        CultureInfo.InvariantCulture, $"{_rate:0.#} requests per second each   ·   about {_clients * _rate:0.#} per second in total");

    private void BuildDuration(Panel host)
    {
        var value = BigValue(host, DurationText());
        var slider = Ui.Slider(string.Empty, 0, 120, 5, _duration, _ => string.Empty, Inner);
        slider.Location = new Point(0, 70);
        slider.ValueChanged += (_, _) =>
        {
            _duration = (int)slider.Value;
            value.Text = DurationText();
        };

        host.Controls.Add(slider);
    }

    private string DurationText() => _duration == 0 ? "until I press Stop" : $"{_duration} seconds";

    private void BuildWork(Panel host)
    {
        var value = BigValue(host, WorkText());
        var slider = Ui.Slider(string.Empty, 0, 2000, 50, _work, _ => string.Empty, Inner);
        slider.Location = new Point(0, 70);
        slider.ValueChanged += (_, _) =>
        {
            _work = (int)slider.Value;
            value.Text = WorkText();
        };

        host.Controls.Add(slider);
        host.Controls.Add(Note("0 ms means the request finishes immediately. A few hundred milliseconds is a slow database.", 48, top: 120));
    }

    private string WorkText() => _work == 0 ? "no fake work" : $"{_work} ms per request";

    private void BuildSummary(Panel host)
    {
        var text = new Label
        {
            Text = CurrentPlan().Describe(_manifest),
            Font = new Font("Segoe UI", 12f),
            ForeColor = Theme.Text,
            BackColor = Theme.Panel2,
            Padding = new Padding(18),
            Location = new Point(0, 0),
            Size = new Size(Inner, 260),
        };

        host.Controls.Add(text);
    }

    private static Label BigValue(Panel host, string text)
    {
        var label = new Label
        {
            Text = text,
            Font = Theme.Big,
            ForeColor = Theme.Accent,
            BackColor = Theme.Panel,
            AutoSize = true,
            Location = new Point(0, 8),
        };

        host.Controls.Add(label);

        return label;
    }

    private static Label Note(string text, int height, int top = 0) => new()
    {
        Text = text,
        Font = Theme.Body,
        ForeColor = Theme.Muted,
        BackColor = Theme.Panel,
        Location = new Point(0, top),
        Size = new Size(Inner, height),
    };

    private sealed record Step(string Title, string Hint, Action<Panel> Build, Func<bool> IsActive, Func<bool> IsValid);

    private sealed class StepDots : Surface
    {
        private int _count = 1, _current;

        public void Update(int count, int current)
        {
            _count = count;
            _current = current;
            Invalidate();
        }

        protected override void Draw(Graphics g)
        {
            float gap = Math.Min(30, (Width - 14f) / Math.Max(1, _count));
            float x = Width - 7 - ((_count - 1) * gap);

            for (int i = 0; i < _count; i++)
            {
                float cx = x + (i * gap);
                var color = i == _current ? Theme.Accent : i < _current ? Theme.Muted : Theme.Line;
                Fill(g, color, new RectangleF(cx - (i == _current ? 6 : 4), Height / 2f - (i == _current ? 6 : 4), i == _current ? 12 : 8, i == _current ? 12 : 8), 6);
            }
        }
    }
}
