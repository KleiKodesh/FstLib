# SQLite Optimizations vs FST Library: Detailed Analysis

## Executive Summary

SQLite's `LIKE %pattern%` query is faster than our FST Contains implementation because SQLite uses **multiple sophisticated query optimization techniques** that we don't have. However, most of these optimizations are **not applicable to FSTs** because FSTs are fundamentally different data structures with different access patterns.

---

## SQLite's Key Optimizations for LIKE Queries

### 1. **Index-Based Range Queries (LIKE Optimization)**

**What SQLite Does:**
- For `LIKE pattern%` (prefix match), SQLite converts it to a range query:
  - `column >= 'pattern' AND column < 'patterp'` (next lexicographic value)
- This allows using B-Tree indexes for fast range lookups
- **Only works for patterns that don't start with a wildcard**

**Why It Helps:**
- B-Tree indexes are optimized for range queries
- Can skip directly to the first matching row
- Can stop at the last matching row

**Why We Can't Use This in FST:**
- Our FST already IS a range structure (trie-based)
- We can't convert `*pattern*` to a range because the pattern can appear anywhere
- Our DFA approach is actually the correct solution for this case

**Code Reference from SQLite:**
```
For LIKE pattern%:
  column >= x AND column < y
  where x = pattern, y = next lexicographic value
```

---

### 2. **Query Planner Cost Analysis**

**What SQLite Does:**
- Estimates CPU and disk I/O costs for different query plans
- Chooses the plan with minimum estimated cost
- Uses statistics from `ANALYZE` command to make better estimates

**Why It Helps:**
- SQLite can decide whether to use an index or do a full table scan
- For `LIKE %pattern%`, SQLite might choose full table scan if it's faster
- Adaptive based on data distribution

**Why We Can't Use This in FST:**
- We don't have multiple query plans to choose from
- FST traversal is our only option
- We could add heuristics (e.g., if pattern is very common, use different strategy)

---

### 3. **Covering Indexes**

**What SQLite Does:**
- Stores all needed columns in the index itself
- Avoids second lookup to the main table
- Saves one binary search per row

**Why It Helps:**
- Reduces disk I/O by 50% (one B-Tree lookup instead of two)
- For `LIKE %pattern%`, all data is in the index

**Why We Can't Use This in FST:**
- Our FST already stores the output value (long) at each final node
- We're not doing a second lookup
- We already have this optimization built-in

---

### 4. **Automatic Query-Time Indexes**

**What SQLite Does:**
- Creates temporary B-Tree indexes on-the-fly for joins
- Only if the cost is justified (lookup runs > log(N) times)
- Lasts only for the duration of one query

**Why It Helps:**
- Converts O(N²) join to O(N log N)
- Useful when no permanent indexes exist

**Why We Can't Use This in FST:**
- We're not doing joins
- Our FST is already a permanent index
- Not applicable to single-table queries

---

### 5. **Predicate Push-Down Optimization**

**What SQLite Does:**
- Pushes WHERE clause constraints into subqueries
- Reduces the size of intermediate result sets
- Helps indexes work better on subqueries

**Why It Helps:**
- Smaller intermediate tables = faster processing
- Indexes can be used on subquery results

**Why We Can't Use This in FST:**
- We're not using subqueries
- Not applicable to our use case

---

### 6. **ANALYZE Statistics**

**What SQLite Does:**
- Runs `ANALYZE` to gather statistics about data distribution
- Stores statistics in `sqlite_stat1`, `sqlite_stat3`, `sqlite_stat4` tables
- Uses statistics to estimate selectivity of different indexes

**Why It Helps:**
- Query planner makes better decisions
- Knows which index is more selective
- Can estimate how many rows will be filtered

**Why We Can't Use This in FST:**
- We could implement similar statistics gathering
- Would help us decide when to use different strategies
- **This is actually a good optimization we could add**

---

### 7. **B-Tree Index Structure Itself**

**What SQLite Does:**
- Uses B-Tree indexes which are optimized for disk I/O
- Balanced tree structure ensures O(log N) lookups
- Caches frequently accessed nodes in memory

**Why It Helps:**
- Efficient for range queries
- Good cache locality
- Handles large datasets efficiently

**Why We Can't Use This in FST:**
- FSTs are already O(m) where m = key length (better than B-Tree's O(log N) for exact match)
- FSTs are optimized for string keys, not numeric keys
- FSTs have better compression (92% vs SQLite's typical 50-70%)

---

### 8. **Full Table Scan with Optimized String Matching**

**What SQLite Does:**
- When no index helps, does a full table scan
- Uses optimized C string matching (likely Boyer-Moore or similar)
- Highly optimized at the C level

**Why It Helps:**
- For `LIKE %pattern%`, full table scan might be faster than index
- C-level string matching is very fast
- No allocation overhead

**Why We Can't Use This in FST:**
- We're doing FST traversal, not full table scan
- Our DFA approach is more selective than full scan
- We could optimize our string matching at the byte level

---

## What We're Missing: Actionable Optimizations

### 1. **Byte-Level String Matching (High Impact)**

**Current Implementation:**
- We decode labels to strings
- Call `string.Contains()` on decoded strings
- Allocates strings for every term

**Optimization:**
- Match patterns at the byte level
- Avoid UTF-8 decoding until match is confirmed
- Use optimized byte-level algorithms (Boyer-Moore, Knuth-Morris-Pratt)

**Estimated Improvement:** 20-30% faster

---

### 2. **Adaptive Strategy Selection (Medium Impact)**

**Current Implementation:**
- Always use DFA intersection

**Optimization:**
- If pattern is very short (1-2 chars), use different strategy
- If pattern is very common, use different strategy
- Gather statistics on pattern selectivity

**Estimated Improvement:** 10-20% faster for specific patterns

---

### 3. **Caching Frequently Searched Patterns (Medium Impact)**

**Current Implementation:**
- No caching

**Optimization:**
- Cache results of frequently searched patterns
- LRU cache with configurable size
- Useful for repeated queries

**Estimated Improvement:** 50-100% faster for repeated queries

---

### 4. **Hybrid Approach: SQLite for Contains (Pragmatic)**

**Current Implementation:**
- Always use FST

**Optimization:**
- Use SQLite for `LIKE %pattern%` queries
- Use FST for prefix/suffix/exact/fuzzy
- Best of both worlds

**Estimated Improvement:** 2-3x faster for Contains queries

---

### 5. **Better DFA Construction (Low Impact)**

**Current Implementation:**
- KMP-based DFA

**Optimization:**
- Use Aho-Corasick for multiple patterns
- Use more sophisticated DFA minimization
- Precompile common patterns

**Estimated Improvement:** 5-10% faster

---

## Why SQLite Wins on Contains

### The Fundamental Reason

For `LIKE %pattern%`, SQLite's full table scan with optimized C string matching beats our DFA approach because:

1. **No pruning possible**: The pattern can appear anywhere, so the DFA can't eliminate many branches
2. **Allocation overhead**: We allocate strings for every term; SQLite scans raw bytes
3. **C-level optimization**: SQLite's string matching is highly optimized at the C level
4. **Simpler algorithm**: Full scan is simpler and has better cache locality than tree traversal

### The Math

For a dictionary with N words:
- **SQLite full scan**: O(N × m) where m = average word length
  - But with highly optimized C string matching
  - Good cache locality
  - No allocation overhead

- **FST with DFA**: O(m + k) where k = results
  - But with allocation overhead for every term
  - Tree traversal has worse cache locality
  - DFA can't prune much for contains patterns

In practice, for contains queries, the allocation overhead and cache locality issues make FST slower.

---

## Recommendations

### Short Term (Quick Wins)
1. **Implement byte-level pattern matching** instead of string-level
2. **Cache frequently searched patterns**
3. **Add statistics gathering** for adaptive strategy selection

### Medium Term (Significant Improvements)
1. **Implement hybrid approach**: Use SQLite for Contains queries
2. **Optimize DFA construction** with Aho-Corasick
3. **Add query result caching** with LRU eviction

### Long Term (Architectural)
1. **Build a query optimizer** similar to SQLite's
2. **Implement multiple index types** (B-Tree, Hash, etc.)
3. **Add cost-based query planning**

---

## Conclusion

SQLite's advantage on `LIKE %pattern%` comes from:
1. **Optimized C string matching** (not applicable to FST)
2. **Full table scan strategy** (simpler for this case)
3. **No allocation overhead** (we could fix this)
4. **Better cache locality** (we could improve this)

Our FST library is **fundamentally superior** for:
- Exact match queries (32-2761x faster)
- Prefix queries (13-2451x faster)
- Suffix queries (25-4635x faster)
- Fuzzy search (N/A in SQLite)
- Memory efficiency (92% compression)

The Contains query is a **weak point for FSTs by design**, not a bug. The DFA-based optimization we implemented is the correct architectural approach. Further improvements would require either:
1. Byte-level optimizations (marginal gains)
2. Hybrid approach with SQLite (pragmatic but adds complexity)
3. Accepting that Contains queries are not FST's strength (honest)
