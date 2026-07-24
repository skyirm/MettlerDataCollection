using MettlerDataCollection.Device;

namespace MettlerDataCollection.Tests.Device;

[TestClass]
public class S470KTests
{
    // ===== 双工模式 PH_AND_COND =====

    [TestMethod]
    public void ParsePhAndCond_PhMessage_BuffersDataWithoutEmitting()
    {
        var s470k = new S470K { CurrentMode = CollectMode.PH_AND_COND };
        var emitted = new List<MeasureData>();
        s470k.OnDataProduced += emitted.Add;

        // 只有 pH 消息（没有配对的电导率）—— 不应触发 OnDataProduced
        s470k.ParseData("10s 1 7.42 25.0");

        Assert.AreEqual(0, emitted.Count);
    }

    [TestMethod]
    public void ParsePhAndCond_PairedPhAndCond_EmitsCombinedMeasureData()
    {
        var s470k = new S470K { CurrentMode = CollectMode.PH_AND_COND };
        var emitted = new List<MeasureData>();
        s470k.OnDataProduced += emitted.Add;

        s470k.ParseData("10s 1 7.42 25.0");  // pH
        s470k.ParseData("2 1450.0 24.5");     // cond

        Assert.AreEqual(1, emitted.Count);
        var data = emitted[0];
        Assert.AreEqual(10, data.Time);
        Assert.AreEqual(7.42, data.Ph);
        Assert.AreEqual(1450.0, data.Conductivity);
        Assert.AreEqual(25.0, data.PhTemp);
        Assert.AreEqual(24.5, data.ConductivityTemp);
    }

    [TestMethod]
    public void ParsePhAndCond_CondWithoutPh_TriggersOnParseError()
    {
        var s470k = new S470K { CurrentMode = CollectMode.PH_AND_COND };
        string? error = null;
        s470k.OnParseError += e => error = e;

        s470k.ParseData("2 1450.0");

        Assert.IsNotNull(error);
        StringAssert.Contains(error, "电导率消息无配对 pH");
    }

    [TestMethod]
    public void ParsePhAndCond_PhWithoutTemp_EmitsNullPhTemp()
    {
        var s470k = new S470K { CurrentMode = CollectMode.PH_AND_COND };
        var emitted = new List<MeasureData>();
        s470k.OnDataProduced += emitted.Add;

        s470k.ParseData("10s 1 7.42");        // 3 字段，没温度
        s470k.ParseData("2 1450.0");

        Assert.AreEqual(1, emitted.Count);
        Assert.IsNull(emitted[0].PhTemp);
    }

    [TestMethod]
    public void ParsePhAndCond_RepeatedPh_DropsPreviousUnpaired()
    {
        var s470k = new S470K { CurrentMode = CollectMode.PH_AND_COND };
        var emitted = new List<MeasureData>();
        s470k.OnParseError += _ => { };  // 静默丢弃
        s470k.OnDataProduced += emitted.Add;

        s470k.ParseData("10s 1 7.42");   // 第一条 pH
        s470k.ParseData("20s 1 6.50");   // 第二条 pH（覆盖第一条）
        s470k.ParseData("2 1500.0");     // 配对的是第二条

        Assert.AreEqual(1, emitted.Count);
        Assert.AreEqual(20, emitted[0].Time);
        Assert.AreEqual(6.50, emitted[0].Ph);
    }

    // ===== 单工模式 PH_ONLY =====

    [TestMethod]
    public void ParsePhOnly_ValidLine_EmitsMeasureData()
    {
        var s470k = new S470K { CurrentMode = CollectMode.PH_ONLY };
        var emitted = new List<MeasureData>();
        s470k.OnDataProduced += emitted.Add;

        s470k.ParseData("10s 7.42");

        Assert.AreEqual(1, emitted.Count);
        var data = emitted[0];
        Assert.AreEqual(10, data.Time);
        Assert.AreEqual(7.42, data.Ph);
        Assert.AreEqual(0, data.Conductivity);
        Assert.IsNull(data.PhTemp);
        Assert.IsNull(data.ConductivityTemp);
    }

    [TestMethod]
    public void ParsePhOnly_NonNumericValue_TriggersOnParseError()
    {
        var s470k = new S470K { CurrentMode = CollectMode.PH_ONLY };
        string? error = null;
        var emitted = new List<MeasureData>();
        s470k.OnParseError += e => error = e;
        s470k.OnDataProduced += emitted.Add;

        s470k.ParseData("10s notanumber");

        Assert.IsNotNull(error);
        StringAssert.Contains(error, "PH_ONLY pH 值无法解析");
        Assert.AreEqual(0, emitted.Count);
    }

    [TestMethod]
    public void ParsePhOnly_TooFewFields_TriggersOnParseError()
    {
        var s470k = new S470K { CurrentMode = CollectMode.PH_ONLY };
        string? error = null;
        s470k.OnParseError += e => error = e;

        s470k.ParseData("10s");

        Assert.IsNotNull(error);
        StringAssert.Contains(error, "PH_ONLY 行字段少于 2");
    }

    [TestMethod]
    public void ParsePhOnly_TimeWithoutSuffix_StillParses()
    {
        // 旧协议里时间是 "10s"，但万一仪器发 "10" 也能容错（用 0 fallback 也不崩）
        var s470k = new S470K { CurrentMode = CollectMode.PH_ONLY };
        var emitted = new List<MeasureData>();
        s470k.OnDataProduced += emitted.Add;

        s470k.ParseData("10 7.42");

        Assert.AreEqual(1, emitted.Count);
        // parts[0] = "10"，去掉 "s" 后还是 "10"，int.TryParse 成功
        Assert.AreEqual(10, emitted[0].Time);
    }

    [TestMethod]
    public void ParsePhOnly_TimeUnparseable_UsesZero()
    {
        var s470k = new S470K { CurrentMode = CollectMode.PH_ONLY };
        var emitted = new List<MeasureData>();
        s470k.OnDataProduced += emitted.Add;

        s470k.ParseData("abc 7.42");

        Assert.AreEqual(1, emitted.Count);
        Assert.AreEqual(0, emitted[0].Time);  // 解析失败 fallback 到 0
    }

    // ===== 单工模式 COND_ONLY =====

    [TestMethod]
    public void ParseCondOnly_ValidLine_EmitsMeasureData()
    {
        var s470k = new S470K { CurrentMode = CollectMode.COND_ONLY };
        var emitted = new List<MeasureData>();
        s470k.OnDataProduced += emitted.Add;

        s470k.ParseData("5s 1450.0");

        Assert.AreEqual(1, emitted.Count);
        var data = emitted[0];
        Assert.AreEqual(5, data.Time);
        Assert.AreEqual(1450.0, data.Conductivity);
        Assert.AreEqual(0, data.Ph);
        Assert.IsNull(data.PhTemp);
        Assert.IsNull(data.ConductivityTemp);
    }

    [TestMethod]
    public void ParseCondOnly_NonNumericValue_TriggersOnParseError()
    {
        var s470k = new S470K { CurrentMode = CollectMode.COND_ONLY };
        string? error = null;
        s470k.OnParseError += e => error = e;

        s470k.ParseData("5s hello");

        Assert.IsNotNull(error);
        StringAssert.Contains(error, "COND_ONLY 电导率值无法解析");
    }

    [TestMethod]
    public void ParseCondOnly_TooFewFields_TriggersOnParseError()
    {
        var s470k = new S470K { CurrentMode = CollectMode.COND_ONLY };
        string? error = null;
        s470k.OnParseError += e => error = e;

        s470k.ParseData("5s");

        Assert.IsNotNull(error);
        StringAssert.Contains(error, "COND_ONLY 行字段少于 2");
    }

    // ===== 模式路由 =====

    [TestMethod]
    public void ParseData_DefaultMode_IsPhAndCond()
    {
        // 防御性测试：以后别不小心把默认值改了
        var s470k = new S470K();
        Assert.AreEqual(CollectMode.PH_AND_COND, s470k.CurrentMode);
    }

    [TestMethod]
    public void ParseData_UnknownMode_TriggersOnParseError()
    {
        var s470k = new S470K();
        // 强转一个无效 enum 值
        s470k.CurrentMode = (CollectMode)999;
        string? error = null;
        s470k.OnParseError += e => error = e;

        s470k.ParseData("anything");

        Assert.IsNotNull(error);
        StringAssert.Contains(error, "未知的采集模式");
    }

    // ===== PreprocessData（行切分） =====

    [TestMethod]
    public void PreprocessData_SingleCompleteLine_TriggersOnLinePreprocessedOnce()
    {
        var s470k = new S470K();
        var lines = new List<string>();
        s470k.OnLinePreprocessed += lines.Add;

        s470k.PreprocessData("10s 7.42\r\n");

        Assert.AreEqual(1, lines.Count);
        Assert.AreEqual("10s 7.42", lines[0]);
    }

    [TestMethod]
    public void PreprocessData_MultipleLinesInOneChunk_SplitsAll()
    {
        var s470k = new S470K();
        var lines = new List<string>();
        s470k.OnLinePreprocessed += lines.Add;

        s470k.PreprocessData("10s 7.42\r\n20s 7.50\r\n30s 7.55\r\n");

        Assert.AreEqual(3, lines.Count);
        Assert.AreEqual("10s 7.42", lines[0]);
        Assert.AreEqual("20s 7.50", lines[1]);
        Assert.AreEqual("30s 7.55", lines[2]);
    }

    [TestMethod]
    public void PreprocessData_PartialLine_BuffersUntilNextChunk()
    {
        var s470k = new S470K();
        var lines = new List<string>();
        s470k.OnLinePreprocessed += lines.Add;

        s470k.PreprocessData("10s 7.");          // 半行
        Assert.AreEqual(0, lines.Count);          // 还没出

        s470k.PreprocessData("42\r\n20s 7.50\r\n"); // 续上 + 完整新行

        Assert.AreEqual(2, lines.Count);
        Assert.AreEqual("10s 7.42", lines[0]);   // 拼起来完整
        Assert.AreEqual("20s 7.50", lines[1]);
    }

    [TestMethod]
    public void PreprocessData_EmptyLineBetween_NotEmitted()
    {
        var s470k = new S470K();
        var lines = new List<string>();
        s470k.OnLinePreprocessed += lines.Add;

        s470k.PreprocessData("10s 7.42\r\n\r\n20s 7.50\r\n");

        Assert.AreEqual(2, lines.Count);
        Assert.AreEqual("10s 7.42", lines[0]);
        Assert.AreEqual("20s 7.50", lines[1]);
    }

    [TestMethod]
    public void PreprocessData_LeadingTrailingWhitespace_Trimmed()
    {
        var s470k = new S470K();
        var lines = new List<string>();
        s470k.OnLinePreprocessed += lines.Add;

        s470k.PreprocessData("  10s 7.42  \r\n");

        Assert.AreEqual(1, lines.Count);
        Assert.AreEqual("10s 7.42", lines[0]);
    }

    [TestMethod]
    public void PreprocessData_EmptyChunk_NothingEmitted()
    {
        var s470k = new S470K();
        var lines = new List<string>();
        s470k.OnLinePreprocessed += lines.Add;

        s470k.PreprocessData("");

        Assert.AreEqual(0, lines.Count);
    }
}
