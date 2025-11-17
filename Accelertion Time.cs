using UnityEngine;
using XCharts.Runtime;

public class BoxAccelerationChartController : MonoBehaviour
{
    [Header("References")]
    public BaseChart chart;            // Line chart on your Canvas
    public Rigidbody boxRigidbody;     // The box's Rigidbody

    [Header("Sampling")]
    public float updateInterval = 0.1f;
    public int   maxPoints      = 120;

    [Header("Display")]
    public float yMin = -10f;
    public float yMax =  10f;
    public float deadband = 0.03f;     // suppress tiny noise

    private float timer;
    private int   step;
    private float prevSpeed;

    void Start()
    {
        if (!chart || !boxRigidbody)
        {
            Debug.LogError("[BoxAccelerationChartController] Assign chart and boxRigidbody.");
            enabled = false; return;
        }

        chart.ClearData();
        chart.EnsureChartComponent<Title>().text = "Acceleration vs Time";
        chart.EnsureChartComponent<Legend>().show = false;

        var xAxis = chart.EnsureChartComponent<XAxis>();
        xAxis.type = Axis.AxisType.Category;

        var yAxis = chart.EnsureChartComponent<YAxis>();
        yAxis.type = Axis.AxisType.Value;
        yAxis.minMaxType = Axis.AxisMinMaxType.Custom;
        yAxis.min = yMin;
        yAxis.max = yMax;
        yAxis.axisName.show = true;
        yAxis.axisName.name = "Acceleration (m/s²)";

        var serie = chart.AddSerie<Line>("Acceleration");
        serie.symbol.type = SymbolType.None;

        prevSpeed = boxRigidbody.velocity.magnitude;
    }

    void Update()
    {
        timer += Time.deltaTime;
        if (timer < updateInterval) return;
        timer = 0f;

        float curSpeed = boxRigidbody.velocity.magnitude;
        float a = (curSpeed - prevSpeed) / updateInterval;

        if (Mathf.Abs(a) < deadband) a = 0f;

        string tLabel = (step * updateInterval).ToString("F1") + "s";
        AddPoint(tLabel, a);

        prevSpeed = curSpeed;
        step++;
    }

    void AddPoint(string xLabel, float yValue)
    {
        var serie = chart.GetSerie("Acceleration");
        var xAxis = chart.GetChartComponent<XAxis>();
        if (serie.dataCount >= maxPoints)
        {
            serie.RemoveData(0);
            xAxis.RemoveData(0);
        }
        xAxis.AddData(xLabel);
        serie.AddData(yValue);
    }
}