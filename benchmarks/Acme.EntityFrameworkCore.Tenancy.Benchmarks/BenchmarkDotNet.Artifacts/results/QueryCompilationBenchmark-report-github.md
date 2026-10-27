```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
11th Gen Intel Core i9-11900H 2.50GHz (Max: 800.00GHz), 1 CPU, 16 logical and 8 physical cores
.NET SDK 10.0.202
  [Host]   : .NET 10.0.6 (10.0.6, 10.0.626.17701), X64 RyuJIT x86-64-v4
  ShortRun : .NET 10.0.6 (10.0.6, 10.0.626.17701), X64 RyuJIT x86-64-v4

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method   | Setup                | Mean        | Error       | StdDev     | Gen0    | Gen1   | Allocated |
|--------- |--------------------- |------------:|------------:|-----------:|--------:|-------:|----------:|
| **Compile**  | **EFCore**               | **2,644.79 μs** | **2,632.34 μs** | **144.288 μs** | **11.7188** | **7.8125** | **145.72 KB** |
| CacheHit | EFCore               |    17.86 μs |    76.07 μs |   4.169 μs |  0.7324 |      - |   9.87 KB |
| **Compile**  | **Tenancy**              | **2,683.12 μs** | **3,541.82 μs** | **194.139 μs** | **11.7188** | **7.8125** | **146.67 KB** |
| CacheHit | Tenancy              |    14.78 μs |    13.40 μs |   0.735 μs |  0.7324 |      - |   9.91 KB |
| **Compile**  | **Tenan(...)ilter [22]** | **2,777.43 μs** | **4,727.43 μs** | **259.126 μs** | **11.7188** | **7.8125** | **160.37 KB** |
| CacheHit | Tenan(...)ilter [22] |    14.77 μs |    15.17 μs |   0.831 μs |  0.7324 |      - |   10.2 KB |
