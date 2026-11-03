using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Fastdown.Core;

internal sealed class SpeedTests : IAsyncDisposable
{
    private readonly TcpListener _listener=new(IPAddress.Loopback,0);
    private readonly CancellationTokenSource _stop=new();
    private readonly Task _serve;
    private string Url {get;}
    public SpeedTests(){_listener.Start();Url=$"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/speed";_serve=Serve();}
    private async Task Serve(){try{while(!_stop.IsCancellationRequested){var client=await _listener.AcceptTcpClientAsync(_stop.Token);_=Handle(client);}}catch(OperationCanceledException){}}
    private async Task Handle(TcpClient client)
    {
        using(client)try
        {
            await using var stream=client.GetStream();using var reader=new StreamReader(stream,Encoding.ASCII,false,1024,true);
            var first=await reader.ReadLineAsync();if(first==null)return;var size=first.Contains("/short",StringComparison.Ordinal)?64L*1024*1024:8L*1024*1024*1024;
            string? range=null;while(await reader.ReadLineAsync() is {Length:>0} line)if(line.StartsWith("Range:",StringComparison.OrdinalIgnoreCase))range=line[6..].Trim();
            long from=0,to=size-1;if(range!=null){var bounds=range[6..].Split('-');from=long.Parse(bounds[0]);if(bounds[1]!="")to=long.Parse(bounds[1]);}
            var header=$"HTTP/1.1 {(range==null?"200 OK":"206 Partial Content")}\r\nConnection: close\r\nContent-Length: {to-from+1}\r\nETag: \"speed-test\"\r\n"+(range==null?"":$"Content-Range: bytes {from}-{to}/{size}\r\n")+"\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(header),_stop.Token);var buffer=new byte[65536];
            while(from<=to){var count=(int)Math.Min(buffer.Length,to-from+1);await stream.WriteAsync(buffer.AsMemory(0,count),_stop.Token);from+=count;}
        }catch(Exception ex)when(ex is IOException or OperationCanceledException or SocketException){}
    }
    public async Task Run(string root)
    {
        Directory.CreateDirectory(root);
        var samples=new List<object>();
        foreach(var item in new[]{(5000,0,1,1),(5000,0,8,1),(12000,0,4,1),(0,5000,4,1),(0,12000,8,1),(12000,5000,4,1),(5000,12000,4,1),(5000,0,4,2)})
        {
            var (globalKB,fileKB,connections,count)=item;var limit=globalKB*1024L;
            var area=Path.Combine(root,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(area);
            using var engine=new DownloadEngine(Path.Combine(area,"parts"),()=>limit);using var cancel=new CancellationTokenSource();
            var jobs=Enumerable.Range(0,count).Select(i=>new DownloadJob{Url=Url,FileName=$"sample-{i}.bin",DirectoryPath=area,SpeedLimitBytes=fileKB*1024L}).ToArray();
            var tasks=jobs.Select(j=>engine.DownloadAsync(j,connections,cancel.Token)).ToArray();
            try
            {
                await Task.Delay(500);var before=jobs.Sum(j=>Interlocked.Read(ref j.DownloadedBytes));var watch=Stopwatch.StartNew();await Task.Delay(2500);
                var measured=(jobs.Sum(j=>Interlocked.Read(ref j.DownloadedBytes))-before)/1024.0/watch.Elapsed.TotalSeconds;
                var expected=globalKB>0?fileKB>0?Math.Min(globalKB,fileKB*count):globalKB:fileKB*count;
                Console.WriteLine($"SPEED global={globalKB} file={fileKB} connections={connections} jobs={count}: {measured:F0} KB/s (target {expected})");
                samples.Add(new{globalKB,fileKB,connections,count,measuredKB=measured,targetKB=expected});
                if(measured<expected*.8||measured>expected*1.1)throw new Exception($"Rate outside tested bounds: {measured:F0} / {expected} KB/s");
            }
            finally{cancel.Cancel();try{await Task.WhenAll(tasks);}catch(OperationCanceledException){}foreach(var job in jobs)engine.DeleteParts(job);}
        }
        // One active transfer must respond to a higher cap, no cap, and a lower cap without restart.
        var live=Path.Combine(root,"live");Directory.CreateDirectory(live);long current=5000L*1024;
        using(var engine=new DownloadEngine(Path.Combine(live,"parts"),()=>Interlocked.Read(ref current)))
        using(var cancel=new CancellationTokenSource())
        {
            var job=new DownloadJob{Url=Url,FileName="live.bin",DirectoryPath=live};var task=engine.DownloadAsync(job,4,cancel.Token);
            try
            {
                foreach(var kb in new[]{5000,12000,0,5000})
                {
                    Interlocked.Exchange(ref current,kb*1024L);await Task.Delay(300);var before=Interlocked.Read(ref job.DownloadedBytes);var watch=Stopwatch.StartNew();await Task.Delay(kb==0?500:2000);
                    var measured=(Interlocked.Read(ref job.DownloadedBytes)-before)/1024.0/watch.Elapsed.TotalSeconds;
                    Console.WriteLine($"SPEED live transition {kb}: {measured:F0} KB/s");
                    if(kb>0&&(measured<kb*.8||measured>kb*1.12)||kb==0&&measured<15000)throw new Exception("Live limit change ignored or unlimited mode remained capped");
                }
            }
            finally{cancel.Cancel();try{await task;}catch(OperationCanceledException){}engine.DeleteParts(job);}
        }
        await File.WriteAllTextAsync(Path.Combine(root,"results.json"),System.Text.Json.JsonSerializer.Serialize(samples));
    }
    public async ValueTask DisposeAsync(){_stop.Cancel();_listener.Stop();await _serve;_stop.Dispose();}
}
