# P003 comparison in progress

Automatically refreshed from measured probe output. Status is not a biological acceptance claim.

- mechanics: failed, step 60 / 2820; assembly `db04fcf0c79d1069db4b6660fb8ecea835d77578d432a86879163043e26a56b4`.
- guidance: failed, step 360 / 2820; assembly `db04fcf0c79d1069db4b6660fb8ecea835d77578d432a86879163043e26a56b4`.
- Baseline: latest measured day 470.

[Monthly measurements](p003-monthly.csv) include living leaf area, crowns, projected coverage, suspended tips, rejected births, deaths, collision diagnostics, time, allocations, peak memory and encoded history bytes.
[Species leaf counts including baseline](p003-leaves-with-baseline.csv) count segments, not complete Carex blades. Baseline area/crown diagnostics used different definitions and are deliberately excluded from this table.

The dates below follow the first overwintering. Reference photographs cover 4 April to 12 June (69 days). Irrigation, nutrition and initial planting-size calibration are unchanged.

| Date | Variant | Living leaf area (m²) | Crowns | Projected coverage (m²) | Paused tips | Solid / unresolved foliage |
| --- | --- | ---: | ---: | ---: | ---: | ---: |

Projection is the union of leaf triangles on a 2-cm XY grid, not percentage coverage of a defined facade area. Encoded history counts measure all frame payloads without retaining every frame; they are not the size of a complete RunStore directory. Concurrent runs share the machine, so wall times are not controlled comparative benchmarks.
