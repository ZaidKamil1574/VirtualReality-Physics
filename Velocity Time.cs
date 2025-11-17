using UnityEngine;
using XCharts.Runtime;            // XCharts (Runtime)
using UnityEngine.UI;            // For optional buttons

public class MotionGraphsUI : MonoBehaviour
{
    [Header("Scene References")]
    public Rigidbody boxRb;                   // ← your pushable box rigidbody
    public Transform xrOrigin;                // ← XR Origin / XR Rig root transform

    [Header("Charts (XCharts BaseChart)")]
    public BaseChart boxVelocityChart;        // Line chart on the canvas (Velocity vs Time)
    public BaseChart xrOriginSpeedChart;      // Line chart on the canvas (XR Origin Speed vs Time)

    [Header("Sampling")]
    public float updateInterval = 0.10f;      // seconds between samples
    public int   maxPoints      = 120;        // sliding window length (e.g., last 12s at 0.1s/sample)

    [Header("Optional UI Controls")]
    public Button resetButton;
    public Button pauseButton;
    public Text   pauseLabel;

    private float _timer;
    private int   _step;
    private bool  _paused;

    // XR Origin velocity tracking
    private Vector3 _prevRigPos;
    private bool    _hasPrevRigPos;

    void Awake()
    {
        if (!boxRb) Debug.LogError("[MotionGraphsUI] boxRb not assigned.");
        if (!xrOrigin) Debug.LogError("[MotionGraphsUI] xrOrigin not assigned.");
        if (!boxVelocityChart || !xrOriginSpeedChart)
        {
            Debug.LogError("[MotionGraphsUI] Assign both chart references.");
            enabled = false;
            return;
        }

        SetupVelocityChart(boxVelocityChart, "Velocity vs Time", "Velocity (m/s)", "Box Velocity");
        SetupVelocityChart(xrOriginSpeedChart, "XR Origin Speed vs Time", "Speed (m/s)", "XR Origin Speed");

        if (resetButton) resetButton.onClick.AddListener(ResetGraphs);
        if (pauseButton) pauseButton.onClick.AddListener(TogglePause);
    }

    void OnEnable()
    {
        _timer = 0f;
        _step  = 0;
        _paused = false;
        _hasPrevRigPos = false;
    }

    void Update()
    {
        if (_paused) return;

        _timer += Time.deltaTime;
        if (_timer < updateInterval) return;
        _timer = 0f;

        string tLabel = (_step * updateInterval).ToString("F1") + "s";

        // --- Box velocity magnitude ---
        float vBox = (boxRb) ? boxRb.velocity.magnitude : 0f;
        AddPoint(boxVelocityChart, "Box Velocity", tLabel, vBox);

        // --- XR Origin speed magnitude ---
        float vRig = 0f;
        if (xrOrigin)
        {
            if (_hasPrevRigPos)
            {
                vRig = (xrOrigin.position - _prevRigPos).magnitude / updateInterval;
            }
            _prevRigPos = xrOrigin.position;
            _hasPrevRigPos = true;
        }
        AddPoint(xrOriginSpeedChart, "XR Origin Speed", tLabel, vRig);

        _step++;
    }

    // ---------- helpers ----------
    void SetupVelocityChart(BaseChart chart, string title, string yAxisName, string serieName)
    {
        chart.ClearData();

        // Title / Legend
        chart.EnsureChartComponent<Title>().text = title;
        chart.EnsureChartComponent<Legend>().show = false;

        // Axes
        var xAxis = chart.EnsureChartComponent<XAxis>();
        xAxis.type = Axis.AxisType.Category;
        xAxis.boundaryGap = true;

        var yAxis = chart.EnsureChartComponent<YAxis>();
        yAxis.type = Axis.AxisType.Value;
        yAxis.axisName.show = true;
        yAxis.axisName.name = yAxisName;

        // Serie
        var serie = chart.AddSerie<Line>(serieName);
        serie.symbol.type = SymbolType.None;
        serie.lineStyle.width = 2f;
    }

    void AddPoint(BaseChart chart, string serieName, string xLabel, float yValue)
    {
        var serie = chart.GetSerie(serieName);
        var xAxis = chart.GetChartComponent<XAxis>();

        if (serie == null || xAxis == null) return;

        // sliding window
        if (serie.dataCount >= maxPoints)
        {
            serie.RemoveData(0);
            xAxis.RemoveData(0);
        }

        xAxis.AddData(xLabel);
        serie.AddData(yValue);
    }

    void ResetGraphs()
    {
        _timer = 0f;
        _step  = 0;
        _hasPrevRigPos = false;

        boxVelocityChart.ClearData();
        xrOriginSpeedChart.ClearData();

        SetupVelocityChart(boxVelocityChart, "Velocity vs Time", "Velocity (m/s)", "Box Velocity");
        SetupVelocityChart(xrOriginSpeedChart, "XR Origin Speed vs Time", "Speed (m/s)", "XR Origin Speed");
    }

    void TogglePause()
    {
        _paused = !_paused;
        if (pauseLabel) pauseLabel.text = _paused ? "Resume" : "Pause";
    }
}