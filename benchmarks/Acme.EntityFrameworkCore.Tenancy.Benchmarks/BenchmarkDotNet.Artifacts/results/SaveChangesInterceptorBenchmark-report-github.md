```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
11th Gen Intel Core i9-11900H 2.50GHz (Max: 798.79GHz), 1 CPU, 16 logical and 8 physical cores
.NET SDK 10.0.202
  [Host] : .NET 10.0.6 (10.0.6, 10.0.626.17701), X64 RyuJIT x86-64-v4
  Dry    : .NET 10.0.6 (10.0.6, 10.0.626.17701), X64 RyuJIT x86-64-v4

Job=Dry  IterationCount=1  LaunchCount=1  
RunStrategy=ColdStart  UnrollFactor=1  WarmupCount=1  

```
| Method              | Entities | Mean     | Error | Ratio | Allocated | Alloc Ratio |
|-------------------- |--------- |---------:|------:|------:|----------:|------------:|
| **TrackOnly**           | **1**        | **73.64 ms** |    **NA** |  **1.00** |   **2.12 KB** |        **1.00** |
| TrackAndApplyTenant | 1        | 78.17 ms |    NA |  1.06 |  27.17 KB |       12.83 |
|                     |          |          |       |       |           |             |
| **TrackOnly**           | **100**      | **71.06 ms** |    **NA** |  **1.00** | **133.89 KB** |        **1.00** |
| TrackAndApplyTenant | 100      | 76.56 ms |    NA |  1.08 | 202.29 KB |        1.51 |
