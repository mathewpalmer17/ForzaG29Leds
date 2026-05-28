using System.Text;
using System.Windows;
using System.Windows.Media;

namespace ForzaG29Leds;

public partial class SettingsWindow : Window
{
    public event Action<Settings>? SettingsSaved;

    private readonly TelemetryService _service;

    private static readonly SolidColorBrush Green = new(System.Windows.Media.Color.FromRgb(80,  200, 80));
    private static readonly SolidColorBrush Blue  = new(System.Windows.Media.Color.FromRgb(60,  160, 240));
    private static readonly SolidColorBrush Grey  = new(System.Windows.Media.Color.FromRgb(160, 160, 160));

    public SettingsWindow(Settings settings, TelemetryService service)
    {
        InitializeComponent();
        _service = service;

        PortBox.Text    = settings.Port.ToString();
        SolidBox.Text   = settings.SolidPercent.ToString();
        FlashBox.Text   = settings.FlashPercent.ToString();
        FlashMsBox.Text = settings.FlashIntervalMs.ToString();

        StartupBox.IsChecked = StartupManager.IsEnabled;

        _service.WheelStatusChanged     += OnWheelStatus;
        _service.TelemetryStatusChanged += OnTelemetryStatus;
        _service.PacketReceived         += OnPacketReceived;

        RefreshWheel(service.IsWheelConnected);
        RefreshTel(service.IsReceivingTelemetry);
    }

    // ── Status dots ───────────────────────────────────────────────────────────

    private void OnWheelStatus(bool connected) =>
        Dispatcher.Invoke(() => RefreshWheel(connected));

    private void OnTelemetryStatus(bool receiving) =>
        Dispatcher.Invoke(() => RefreshTel(receiving));

    private void RefreshWheel(bool connected)
    {
        WheelDot.Fill   = connected ? Green : Grey;
        WheelLabel.Text = connected ? "Wheel connected" : "Wheel not found";
    }

    private void RefreshTel(bool receiving)
    {
        TelDot.Fill   = receiving ? Blue : Grey;
        TelLabel.Text = receiving ? "Receiving telemetry" : "No telemetry";
        // Show/hide the Forza setup hint — avoids confusion on first launch
        TelHint.Visibility = receiving ? Visibility.Collapsed : Visibility.Visible;
        if (!receiving)
            TelemetryDump.Text = "Waiting for telemetry…";
    }

    // ── Telemetry dump ────────────────────────────────────────────────────────

    private void OnPacketReceived(ForzaTelemetryPacket pkt) =>
        Dispatcher.Invoke(() => TelemetryDump.Text = FormatPacket(pkt));

    private static string FormatPacket(ForzaTelemetryPacket p)
    {
        float max   = p.EngineMaxRpm;
        float ratio = max > 0f ? p.CurrentEngineRpm / max : 0f;
        var   sb    = new StringBuilder();

        sb.AppendLine("── Engine ──────────────────────────────────────");
        sb.AppendLine($"  RPM          {p.CurrentEngineRpm,7:F0} / {max,7:F0}  ({ratio * 100:F1} %)");
        sb.AppendLine($"  Power        {p.Power / 1000f,7:F2} kW");
        sb.AppendLine($"  Torque       {p.Torque,7:F1} N·m");
        sb.AppendLine($"  Boost        {p.Boost,7:F3}");
        sb.AppendLine($"  Fuel         {p.Fuel * 100f,6:F1} %");
        sb.AppendLine($"  Idle RPM     {p.EngineIdleRpm,7:F0}");
        sb.AppendLine();
        sb.AppendLine("── Drivetrain ───────────────────────────────────");
        sb.AppendLine($"  Gear         {p.Gear}");
        sb.AppendLine($"  Speed        {p.Speed * 3.6f,7:F1} km/h");
        sb.AppendLine($"  Throttle     {p.Accel}");
        sb.AppendLine($"  Brake        {p.Brake}");
        sb.AppendLine($"  Clutch       {p.Clutch}");
        sb.AppendLine($"  Handbrake    {p.HandBrake}");
        sb.AppendLine($"  Steer        {p.Steer}");
        sb.AppendLine($"  Drive type   {p.DrivetrainType switch { 0 => "FWD", 1 => "RWD", 2 => "AWD", _ => p.DrivetrainType.ToString() }}");
        sb.AppendLine($"  Cylinders    {p.NumCylinders}");
        sb.AppendLine();
        sb.AppendLine("── Lap / Race ───────────────────────────────────");
        sb.AppendLine($"  Lap #        {p.LapNumber}");
        sb.AppendLine($"  Position     {p.RacePosition}");
        sb.AppendLine($"  Current      {FmtTime(p.CurrentLap)}");
        sb.AppendLine($"  Last         {FmtTime(p.LastLap)}");
        sb.AppendLine($"  Best         {FmtTime(p.BestLap)}");
        sb.AppendLine($"  Race time    {FmtTime(p.CurrentRaceTime)}");
        sb.AppendLine($"  Distance     {p.DistanceTraveled,7:F0} m");
        sb.AppendLine();
        sb.AppendLine("── Tyres ────────────────────────────────────────");
        sb.AppendLine($"  Temp (°C)    FL {p.TireTempFlC:F1}  FR {p.TireTempFrC:F1}  RL {p.TireTempRlC:F1}  RR {p.TireTempRrC:F1}");
        sb.AppendLine($"  Slip ratio   FL {p.TireSlipRatioFl:F3}  FR {p.TireSlipRatioFr:F3}  RL {p.TireSlipRatioRl:F3}  RR {p.TireSlipRatioRr:F3}");
        sb.AppendLine($"  Slip angle   FL {p.TireSlipAngleFl:F3}  FR {p.TireSlipAngleFr:F3}  RL {p.TireSlipAngleRl:F3}  RR {p.TireSlipAngleRr:F3}");
        sb.AppendLine($"  Comb. slip   FL {p.TireCombinedSlipFl:F3}  FR {p.TireCombinedSlipFr:F3}  RL {p.TireCombinedSlipRl:F3}  RR {p.TireCombinedSlipRr:F3}");
        sb.AppendLine();
        sb.AppendLine("── Suspension ───────────────────────────────────");
        sb.AppendLine($"  Norm travel  FL {p.NormSuspTravelFl:F3}  FR {p.NormSuspTravelFr:F3}  RL {p.NormSuspTravelRl:F3}  RR {p.NormSuspTravelRr:F3}");
        sb.AppendLine($"  Travel (m)   FL {p.SuspensionTravelMetersFl:F3}  FR {p.SuspensionTravelMetersFr:F3}  RL {p.SuspensionTravelMetersRl:F3}  RR {p.SuspensionTravelMetersRr:F3}");
        sb.AppendLine($"  Wheel speed  FL {p.WheelRotationSpeedFl:F1}  FR {p.WheelRotationSpeedFr:F1}  RL {p.WheelRotationSpeedRl:F1}  RR {p.WheelRotationSpeedRr:F1}");
        sb.AppendLine();
        sb.AppendLine("── Motion ───────────────────────────────────────");
        sb.AppendLine($"  Velocity     X {p.VelocityX,8:F2}  Y {p.VelocityY,8:F2}  Z {p.VelocityZ,8:F2} m/s");
        sb.AppendLine($"  Accel        X {p.AccelerationX,8:F2}  Y {p.AccelerationY,8:F2}  Z {p.AccelerationZ,8:F2}");
        sb.AppendLine($"  Ang vel      X {p.AngularVelocityX,8:F3}  Y {p.AngularVelocityY,8:F3}  Z {p.AngularVelocityZ,8:F3}");
        sb.AppendLine($"  Yaw/Pitch/Roll  {p.Yaw,8:F3}  {p.Pitch,8:F3}  {p.Roll,8:F3}");
        sb.AppendLine($"  Position     X {p.PositionX,8:F1}  Y {p.PositionY,8:F1}  Z {p.PositionZ,8:F1}");
        sb.AppendLine();
        sb.AppendLine("── Car ──────────────────────────────────────────");
        sb.AppendLine($"  Class        {p.CarClass switch { 0=>"D",1=>"C",2=>"B",3=>"A",4=>"S1",5=>"S2",6=>"X",_=>p.CarClass.ToString() }}  PI {p.CarPerformanceIndex}");
        sb.AppendLine($"  Cylinders    {p.NumCylinders}");
        sb.AppendLine($"  Ordinal      {p.CarOrdinal}");
        sb.AppendLine($"  Track        {p.TrackOrdinal}");
        sb.AppendLine($"  Timestamp    {p.TimestampMS} ms");

        return sb.ToString();
    }

    private static string FmtTime(float seconds)
    {
        if (seconds <= 0f) return "--:--.---";
        int   m = (int)(seconds / 60);
        float s = seconds - m * 60;
        return $"{m}:{s:00.000}";
    }

    // ── Test LEDs ─────────────────────────────────────────────────────────────

    private void TestLeds_Click(object sender, RoutedEventArgs e) =>
        _service.TestLeds();

    // ── Save / Cancel ─────────────────────────────────────────────────────────

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(PortBox.Text,    out int port)  || port  < 1   || port  > 65535 ||
            !int.TryParse(SolidBox.Text,   out int solid) || solid < 50  || solid > 99    ||
            !int.TryParse(FlashBox.Text,   out int flash) || flash < 50  || flash > 99    ||
            !int.TryParse(FlashMsBox.Text, out int ms)    || ms    < 10  || ms    > 2000)
        {
            System.Windows.MessageBox.Show(
                "Check your values:\n" +
                "  Port: 1 – 65535\n" +
                "  Solid / Flash: 50 – 99 %\n" +
                "  Flash interval: 10 – 2000 ms",
                "Invalid settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (solid >= flash)
        {
            System.Windows.MessageBox.Show("Solid % must be less than Flash %.",
                "Invalid settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        StartupManager.SetEnabled(StartupBox.IsChecked == true);

        var s = new Settings
        {
            Port            = port,
            SolidPercent    = solid,
            FlashPercent    = flash,
            FlashIntervalMs = ms,
        };
        s.Save();
        SettingsSaved?.Invoke(s);
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        _service.WheelStatusChanged     -= OnWheelStatus;
        _service.TelemetryStatusChanged -= OnTelemetryStatus;
        _service.PacketReceived         -= OnPacketReceived;
        base.OnClosed(e);
    }
}
