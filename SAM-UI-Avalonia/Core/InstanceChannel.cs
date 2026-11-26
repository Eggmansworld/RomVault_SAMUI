using System.IO.Pipes;

namespace SAM_UI_Avalonia.Core;

/// <summary>
/// Lets a second launch of SAM-UI (from a taskbar jump list task such as "Pause music")
/// pass a command to the copy that's already running, then exit straight away.
/// Local named pipe, current user only.
/// </summary>
public static class InstanceChannel
{
    private static string PipeName => "SAM-UI-EggmanEdition-" + Environment.UserName;

    /// <summary>Sends a command to the running instance. False if none is running.</summary>
    public static bool Send(string command)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(1500);
            using var w = new StreamWriter(client);
            w.WriteLine(command);
            w.Flush();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Starts listening in the background. <paramref name="onCommand"/> runs on a pool thread.</summary>
    public static void Listen(Action<string> onCommand)
    {
        var t = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly);
                    server.WaitForConnection();
                    using var r = new StreamReader(server);
                    string? line = r.ReadLine();
                    if (!string.IsNullOrWhiteSpace(line)) onCommand(line.Trim());
                }
                catch (IOException)
                {
                    // Another SAM-UI already owns the pipe: let that one take the commands.
                    return;
                }
                catch
                {
                    Thread.Sleep(500);
                }
            }
        })
        { IsBackground = true, Name = "SAM-UI instance channel" };
        t.Start();
    }
}
