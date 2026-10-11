using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace KeyboardWorker;

public sealed class AssetCleanupWindow : Window
{
    readonly Buddy buddy;
    readonly TextBlock status = Pastel.Text("먼저 검사해서 정리할 파일을 확인해 주세요.");
    readonly ListBox files = new() { Height = 250 };
    readonly Button clean;
    UnusedAsset[] reviewed = Array.Empty<UnusedAsset>();
    readonly TextBlock dietStatus = Pastel.Text("먼저 검사해서 줄일 수 있는 용량을 확인해 주세요.");
    readonly Button slim;
    DietPlan? diet;
    public AssetCleanupWindow(Buddy buddy)
    {
        this.buddy = buddy; var p = new StackPanel { Margin = new Thickness(22) };
        p.Children.Add(Pastel.Text("저장 공간 관리 ♡", 22));
        var upkeep = new StackPanel(); upkeep.Children.Add(Pastel.Text("자동 정리", 18)); var usage = Pastel.Text("", 12); var found = Pastel.Text("", 12);
        var autoTidy = new CheckBox { Content = "하루에 한 번 임시 파일·오래된 백업을 알아서 정리하기", IsChecked = buddy.Pref.Tidy.Auto };
        var autoUnused = new CheckBox { Content = "30일 넘게 쓰지 않은 이미지·소리도 알아서 휴지통으로 보내기", IsChecked = buddy.Pref.Tidy.AutoUnused, Margin = new Thickness(0, 4, 0, 4) };
        async Task SaveTidy() { buddy.Pref.Tidy = buddy.Pref.Tidy with { Auto = autoTidy.IsChecked == true, AutoUnused = autoUnused.IsChecked == true }; await Storage.SaveAsync(buddy.Pref); }
        autoTidy.Click += async (_, _) => await SaveTidy(); autoUnused.Click += async (_, _) => await SaveTidy();
        Leftover[] leftovers = Array.Empty<Leftover>();
        async Task Look()
        {
            string root = Storage.Root; int keep = buddy.Pref.Tidy.AutoBackupKeep; var rows = await Task.Run(() => Housekeeping.Usage(root)); leftovers = await Task.Run(() => Housekeeping.Scan(root, DateTime.Now, keep));
            usage.Text = "설정 폴더 사용량 · " + string.Join("  ·  ", rows.Select(r => $"{r.Name} {Megabytes(r.Bytes)} ({r.Files}개)")) + "  ·  합계 " + Megabytes(rows.Sum(r => r.Bytes));
            found.Text = (leftovers.Length == 0 ? "지금 정리할 임시 파일·오래된 백업이 없어요." : "정리할 것 " + leftovers.Length + "개 · " + Megabytes(leftovers.Sum(l => l.Bytes)) + "\n" + string.Join("\n", leftovers.GroupBy(l => l.Kind).Select(g => "· " + g.Key + " " + g.Count() + "개")))
                + (buddy.Pref.Tidy.LastRun.Length > 0 ? "\n마지막 자동 정리: " + buddy.Pref.Tidy.LastRun : "") + (buddy.UpkeepStatus.Length > 0 ? "\n" + buddy.UpkeepStatus : "");
        }
        upkeep.Children.Add(Pastel.Text("저장하다 끊긴 임시 파일, 가져오다 만 작업 폴더, 30일 지난 손상 파일 사본, 오래된 안전·자동 백업을 지웁니다. 설정, 사용 중인 이미지·소리, 대화 기록은 건드리지 않아요.", 12));
        upkeep.Children.Add(autoTidy); upkeep.Children.Add(autoUnused); upkeep.Children.Add(usage); upkeep.Children.Add(found);
        upkeep.Children.Add(Pastel.Button("지금 정리", async () =>
        {
            try { IsEnabled = false; var targets = leftovers; long freed = await Task.Run(() => Housekeeping.Clean(targets)); await Look(); found.Text = "정리했어요 ♡ " + Megabytes(freed) + " 비움\n" + found.Text; }
            catch (Exception e) { found.Text = "정리하지 못했습니다. " + e.Message; }
            finally { IsEnabled = true; }
        }, true));
        p.Children.Add(Pastel.Card(upkeep)); Loaded += async (_, _) => await Look();
        p.Children.Add(Pastel.Text("미사용 이미지·소리 정리", 18));        p.Children.Add(Pastel.Text("프로그램이 복사해 둔 이미지·MP3·WAV 중 사용하지 않는 파일을 찾아 휴지통으로 보냅니다. 현재 설정·캐릭터/소리 프리셋·커스텀 장비에서 참조하는 파일과 최근 24시간 안에 등록한 파일은 보호합니다. 원본 파일과 대화 기록은 정리하지 않습니다.", 13));
        p.Children.Add(Pastel.Button("미사용 파일 검사", async () => await Scan(), true)); p.Children.Add(status); p.Children.Add(files);
        clean = Pastel.Button("확인한 미사용 파일을 휴지통으로 보내기", async () => await Clean()); clean.IsEnabled = false; p.Children.Add(clean);
        var dietPanel = new StackPanel(); dietPanel.Children.Add(Pastel.Text("이미지 폴더 다이어트", 18));
        dietPanel.Children.Add(Pastel.Text("사용 중인 이미지 가운데 내용이 똑같은 파일은 하나로 합치고, 화면에 그려지는 크기(긴 변 " + ImageDiet.MaxSide + "px)보다 큰 이미지는 그 크기로 줄여 저장합니다. 화면에 보이는 모습은 그대로예요. 합쳐지거나 교체된 파일은 휴지통으로 보내므로 되돌릴 수 있습니다. 새로 등록하는 이미지는 자동으로 이렇게 저장돼요.", 12));
        dietPanel.Children.Add(Pastel.Button("중복·큰 이미지 검사", async () => await ScanDiet(), true)); dietPanel.Children.Add(dietStatus);
        slim = Pastel.Button("이미지 폴더 줄이기", async () => await ApplyDiet()); slim.IsEnabled = false; dietPanel.Children.Add(slim); p.Children.Add(Pastel.Card(dietPanel, true));
        p.Children.Add(Pastel.Button("닫기", Close)); Pastel.Window(this, "저장 공간 관리", Pastel.Scroll(p), 680, 820);
    }
    void CheckEditors()
    {
        if (Application.Current.Windows.Cast<Window>().Any(w => w is ImageSettingsWindow or ImageAdjustmentWindow or CustomGearWindow)) throw new InvalidOperationException("이미지·장비 편집 창을 저장하거나 닫은 뒤 정리해 주세요.");
    }
    string[] References() => AssetCleanup.References(buddy.Pref.Snapshot(), buddy.Presets.Items.Select(p => p.Snapshot()).ToArray(), buddy.Game.State.Snapshot(), buddy.Adventure.State.Snapshot());
    async Task Scan()
    {
        try
        {
            CheckEditors(); IsEnabled = false; status.Text = "사용 중인 파일을 확인하는 중…";
            await OrderedStore.FlushAsync(); var references = References();
            reviewed = await Task.Run(() => AssetCleanup.Scan(Storage.Root, references, DateTime.UtcNow));
            files.ItemsSource = reviewed.Select(f => $"{System.IO.Path.GetFileName(f.Path)} · {f.Bytes / 1024.0:N0}KB").ToArray();
            status.Text = $"정리 가능: {reviewed.Length}개 · {reviewed.Sum(f => f.Bytes) / 1024.0 / 1024:N2}MB"; clean.IsEnabled = reviewed.Length > 0;
        }
        catch (Exception e) { reviewed = Array.Empty<UnusedAsset>(); files.ItemsSource = null; clean.IsEnabled = false; status.Text = "검사하지 못했습니다. " + e.Message; }
        finally { IsEnabled = true; }
    }
    static string Megabytes(long bytes) => bytes < 1048576 ? (bytes / 1024.0).ToString("N0") + "KB" : (bytes / 1048576.0).ToString("N1") + "MB";
    async Task ScanDiet()
    {
        try
        {
            CheckEditors(); IsEnabled = false; dietStatus.Text = "이미지를 비교하는 중…";
            await OrderedStore.FlushAsync(); var references = References(); string root = Storage.Root;
            diet = await Task.Run(() => ImageDiet.Scan(root, references));
            dietStatus.Text = $"같은 그림 {diet.Merge.Count}개 (합치면 {Megabytes(diet.DuplicateBytes)} 절약) · 큰 이미지 {diet.Oversized.Length}개 (현재 {Megabytes(diet.OversizedBytes)}, 줄인 뒤 크기는 실행해야 알 수 있어요)";
            slim.IsEnabled = diet.Merge.Count > 0 || diet.Oversized.Length > 0;
        }
        catch (Exception e) { diet = null; slim.IsEnabled = false; dietStatus.Text = "검사하지 못했습니다. " + e.Message; }
        finally { IsEnabled = true; }
    }
    async Task ApplyDiet()
    {
        var plan = diet; if (plan == null) return;
        try
        {
            CheckEditors(); IsEnabled = false; dietStatus.Text = "참조를 옮기고 이미지를 줄이는 중…";
            // Point every user of a duplicate at the kept copy and save that first; only then may the duplicates leave.
            var next = buddy.Pref.Snapshot(); next.ImagePaths = next.ImagePaths.ToDictionary(pair => pair.Key, pair => ImageDiet.Map(plan.Merge, pair.Value));
            buddy.Apply(next, configureKeys: false, persist: false);
            await Storage.SaveAsync(buddy.Pref);
            await buddy.Presets.RemapImagesAsync(plan.Merge);
            buddy.Game.RemapImages(plan.Merge);
            await OrderedStore.FlushAsync();
            var references = References(); var result = await Task.Run(() => ImageDiet.Apply(plan, references));
            SharedSprites.Invalidate(plan.Oversized); buddy.Apply(buddy.Pref.Snapshot(), configureKeys: false, persist: false, reloadImages: true);
            dietStatus.Text = $"완료 ♡ 합친 파일 {result.Merged}개 · 줄인 이미지 {result.Resized}개 · {Megabytes(result.SavedBytes)} 절약" + (result.Failed > 0 ? $" · 처리 실패 {result.Failed}개 (그대로 둠)" : "");
            diet = null; slim.IsEnabled = false;
        }
        catch (Exception e) { dietStatus.Text = "줄이지 못했습니다. " + e.Message; }
        finally { IsEnabled = true; }
    }
    async Task Clean()
    {
        try
        {
            CheckEditors(); IsEnabled = false; status.Text = "참조를 다시 확인하고 휴지통으로 보내는 중…";
            await OrderedStore.FlushAsync(); var references = References(); var candidates = reviewed;
            var result = await Task.Run(() => AssetCleanup.Clean(Storage.Root, candidates, references, DateTime.UtcNow));
            status.Text = $"정리 완료 {result.Removed}개 · 새로 사용/변경되어 제외 {result.Skipped}개 · 처리 실패 {result.Failed}개";
            reviewed = Array.Empty<UnusedAsset>(); files.ItemsSource = null; clean.IsEnabled = false;
        }
        catch (Exception e) { status.Text = "정리하지 못했습니다. " + e.Message; }
        finally { IsEnabled = true; }
    }
}
