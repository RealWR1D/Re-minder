namespace ReMinder.App.UI;

/// <summary>One editor window; serialize saves and coalesce rapid navigation to the latest task.</summary>
internal sealed class EditorSession(StateStore store, TaskService tasks, Action<EditorWindow> show, Action<string, bool> notice)
{
    internal EditorWindow? Current { get; private set; }
    private Request? pending;
    private bool processing;
    private sealed record Request(string? Id, TaskCompletionSource<bool> Completion);

    internal Task<bool> OpenAsync(string? taskId)
    {
        pending?.Completion.TrySetResult(false);
        var request = new Request(taskId, new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously));
        pending = request;
        if (!processing) { processing = true; _ = Process(); }
        return request.Completion.Task;
    }
    private async Task Process()
    {
        try
        {
            while (pending is { } request)
            {
                pending = null;
                try
                {
                    if (Current is { IsLeaving: true } leaving) await leaving.WhenClosed;
                    if (Current is { } editor)
                    {
                        if (editor.TaskId == request.Id) { editor.Activate(); request.Completion.TrySetResult(true); continue; }
                        var modified = editor.HasChanges;
                        if (!await editor.TrySaveForSwitchAsync())
                        {
                            if (editor.IsLeaving) await editor.WhenClosed;
                            else
                            {
                                editor.Activate(); pending?.Completion.TrySetResult(false); pending = null;
                                request.Completion.TrySetResult(false); continue;
                            }
                        }
                        else if (modified) notice("修改已自动保存", false);
                    }
                    if (pending is not null) { request.Completion.TrySetResult(false); continue; }
                    var state = store.Snapshot;
                    var target = request.Id is null ? null : state.Tasks.FirstOrDefault(t => t.Id == request.Id && t.Status != TaskStatus.Deleted);
                    if (request.Id is not null && target is null) { notice("任务已不存在或已删除", false); request.Completion.TrySetResult(false); continue; }
                    if (Current is null)
                    {
                        var window = new EditorWindow(store, tasks, state.Settings, target); Current = window;
                        window.Closed += (_, _) =>
                        {
                            if (ReferenceEquals(Current, window)) Current = null;
                            if (window.WasDeleted) notice("任务已移入最近删除", true);
                            else if (window.WasSaved) notice(window.WasCreated ? "任务已添加" : "任务已保存", false);
                        };
                        show(window);
                    }
                    else { Current.LoadTask(state.Settings, target); Current.Activate(); }
                    request.Completion.TrySetResult(true);
                }
                catch (Exception e) { request.Completion.TrySetException(e); }
            }
        }
        finally { processing = false; }
    }
}
