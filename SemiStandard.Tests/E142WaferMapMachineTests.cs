using XStateNet.Orchestration;
using XStateNet.Semi.Standards;
using Xunit;
using static SemiStandard.Tests.StateMachineTestHelpers;

namespace SemiStandard.Tests;

public class E142WaferMapMachineTests : IDisposable
{
    private readonly EventBusOrchestrator _orchestrator;
    private readonly E142WaferMapMachine _waferMapMgmt;

    public E142WaferMapMachineTests()
    {
        var config = new OrchestratorConfig { EnableLogging = true, PoolSize = 4, EnableMetrics = false };
        _orchestrator = new EventBusOrchestrator(config);
        _waferMapMgmt = new E142WaferMapMachine("EQ001", _orchestrator);
    }

    public void Dispose() => _orchestrator?.Dispose();

    [Fact]
    public async Task E142_Should_Create_WaferMap()
    {
        var waferMap = await _waferMapMgmt.CreateWaferMapAsync("MAP001", "WAFER001", "LOT001");
        Assert.NotNull(waferMap);
        Assert.Equal("MAP001", waferMap.MapId);
        Assert.Equal("WAFER001", waferMap.WaferId);
        Assert.Contains("NoMap", waferMap.GetCurrentState());
    }

    [Fact]
    public async Task E142_Should_Load_WaferMap()
    {
        var waferMap = await _waferMapMgmt.CreateWaferMapAsync("MAP001", "WAFER001", "LOT001");

        var mapData = CreateTestMapData();
        var result = await waferMap.LoadAsync(mapData);
        AssertState(result, "Loaded");

        Assert.NotNull(waferMap.LoadTime);
        Assert.Equal(100, waferMap.GetStatistics().TotalDies);
    }

    [Fact]
    public async Task E142_Should_Apply_WaferMap()
    {
        var waferMap = await _waferMapMgmt.CreateWaferMapAsync("MAP001", "WAFER001", "LOT001");

        var mapData = CreateTestMapData();
        await waferMap.LoadAsync(mapData);

        var result = await waferMap.ApplyAsync();
        AssertState(result, "Applied");

        Assert.NotNull(waferMap.ApplyTime);
    }

    [Fact]
    public async Task E142_Should_Release_WaferMap()
    {
        var waferMap = await _waferMapMgmt.CreateWaferMapAsync("MAP001", "WAFER001", "LOT001");

        var mapData = CreateTestMapData();
        await waferMap.LoadAsync(mapData);
        await waferMap.ApplyAsync();

        var result = await waferMap.ReleaseAsync();
        AssertState(result, "Loaded");

        Assert.Null(waferMap.ApplyTime);
    }

    [Fact]
    public async Task E142_Should_Update_WaferMap()
    {
        var waferMap = await _waferMapMgmt.CreateWaferMapAsync("MAP001", "WAFER001", "LOT001");

        var mapData = CreateTestMapData();
        await waferMap.LoadAsync(mapData);
        await waferMap.ApplyAsync();

        var updateData = new MapUpdateData
        {
            Version = 2,
            DieUpdates = new List<DieUpdate>
            {
                new DieUpdate { X = 0, Y = 0, NewBinCode = 2, TestResult = "FAIL" }
            }
        };

        var result = await waferMap.UpdateAsync(updateData);
        AssertState(result, "Updating");

        result = await waferMap.UpdateCompleteAsync();
        AssertState(result, "Applied");
        Assert.Equal(1, waferMap.UpdateCount);
    }

    [Fact]
    public async Task E142_Should_Update_Die_Test_Result()
    {
        var waferMap = await _waferMapMgmt.CreateWaferMapAsync("MAP001", "WAFER001", "LOT001");

        var mapData = CreateTestMapData();
        await waferMap.LoadAsync(mapData);
        await waferMap.ApplyAsync();

        await waferMap.UpdateDieTestResultAsync(0, 0, 2, "FAIL");

        var die = waferMap.GetDieInfo(0, 0);
        Assert.NotNull(die);
        Assert.Equal(2, die.BinCode);
        Assert.Equal("FAIL", die.TestResult);
    }

    [Fact]
    public async Task E142_Should_Unload_WaferMap()
    {
        var waferMap = await _waferMapMgmt.CreateWaferMapAsync("MAP001", "WAFER001", "LOT001");

        var mapData = CreateTestMapData();
        await waferMap.LoadAsync(mapData);

        var result = await waferMap.UnloadAsync();
        AssertState(result, "Unloading");

        result = await waferMap.UnloadCompleteAsync();
        AssertState(result, "NoMap");
        Assert.Null(waferMap.LoadTime);
    }

    [Fact]
    public async Task E142_Should_Get_Wafer_Map_Statistics()
    {
        var waferMap = await _waferMapMgmt.CreateWaferMapAsync("MAP001", "WAFER001", "LOT001");

        var mapData = CreateTestMapData();
        await waferMap.LoadAsync(mapData);
        await waferMap.ApplyAsync();

        // Update some dies
        await waferMap.UpdateDieTestResultAsync(0, 0, 1, "PASS");
        await waferMap.UpdateDieTestResultAsync(1, 0, 1, "PASS");
        await waferMap.UpdateDieTestResultAsync(2, 0, 2, "FAIL");

        var stats = waferMap.GetStatistics();
        Assert.Equal(100, stats.TotalDies);
        Assert.Equal(3, stats.TestedDies);
        Assert.Equal(2, stats.GoodDies);
        Assert.True(stats.Yield > 0);
    }

    [Fact]
    public async Task E142_Should_Delete_WaferMap()
    {
        var waferMap = await _waferMapMgmt.CreateWaferMapAsync("MAP001", "WAFER001", "LOT001");

        var result = await _waferMapMgmt.DeleteWaferMapAsync("MAP001");

        Assert.True(result);
        var deleted = _waferMapMgmt.GetWaferMap("MAP001");
        Assert.Null(deleted);
    }

    [Fact]
    public async Task E142_Should_Get_All_WaferMaps()
    {
        await _waferMapMgmt.CreateWaferMapAsync("MAP001", "WAFER001", "LOT001");
        await _waferMapMgmt.CreateWaferMapAsync("MAP002", "WAFER002", "LOT001");
        await _waferMapMgmt.CreateWaferMapAsync("MAP003", "WAFER003", "LOT001");

        var maps = _waferMapMgmt.GetAllWaferMaps().ToList();
        Assert.Equal(3, maps.Count);
    }

    [Fact]
    public async Task E142_Should_Handle_Multiple_Updates()
    {
        var waferMap = await _waferMapMgmt.CreateWaferMapAsync("MAP001", "WAFER001", "LOT001");

        var mapData = CreateTestMapData();
        await waferMap.LoadAsync(mapData);
        await waferMap.ApplyAsync();

        // First update
        var updateData1 = new MapUpdateData { Version = 2 };
        await waferMap.UpdateAsync(updateData1);
        await waferMap.UpdateCompleteAsync();
        Assert.Equal(1, waferMap.UpdateCount);

        // Second update
        var updateData2 = new MapUpdateData { Version = 3 };
        await waferMap.UpdateAsync(updateData2);
        await waferMap.UpdateCompleteAsync();
        Assert.Equal(2, waferMap.UpdateCount);
    }

    [Fact]
    public async Task E142_Should_Have_Correct_MachineId()
    {
        Assert.Equal("E142_WAFERMAP_MGMT_EQ001", _waferMapMgmt.MachineId);
    }

    private WaferMapData CreateTestMapData()
    {
        var dies = new List<DieData>();
        for (int x = 0; x < 10; x++)
        {
            for (int y = 0; y < 10; y++)
            {
                dies.Add(new DieData
                {
                    X = x,
                    Y = y,
                    BinCode = 0, // Untested
                    TestResult = "",
                    IsReference = x == 0 && y == 0,
                    IsEdge = x == 0 || y == 0 || x == 9 || y == 9
                });
            }
        }

        return new WaferMapData
        {
            Version = 1,
            WaferId = "WAFER001",
            LotId = "LOT001",
            DieArray = dies.ToArray(),
            BinDefinitions = new List<BinDefinition>
            {
                new BinDefinition { BinCode = 0, BinName = "Untested", BinType = "Unknown", BinColor = "Gray" },
                new BinDefinition { BinCode = 1, BinName = "Pass", BinType = "Good", BinColor = "Green" },
                new BinDefinition { BinCode = 2, BinName = "Fail", BinType = "Bad", BinColor = "Red" }
            },
            RowCount = 10,
            ColumnCount = 10,
            OriginLocation = "UpperLeft"
        };
    }
}
