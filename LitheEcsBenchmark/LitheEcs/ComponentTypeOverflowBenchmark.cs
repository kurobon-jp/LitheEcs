using BenchmarkDotNet.Attributes;
using LitheEcs;

namespace LitheEcsBenchmark;

public struct DirectLookupComponent { public int Value; }
public struct OverflowLookupComponent { public int Value; }

[MemoryDiagnoser]
public class ComponentTypeOverflowBenchmark
{
    private World _world = null!;
    private Entity _directEntity;
    private Entity _overflowEntity;

    [GlobalSetup]
    public void Setup()
    {
        // Register the direct component first, then deliberately push the next
        // component beyond the 256-entry direct lookup range.
        _ = ComponentType<DirectLookupComponent>.Id;
            _ = ComponentType<Registration0>.Id;
            _ = ComponentType<Registration1>.Id;
            _ = ComponentType<Registration2>.Id;
            _ = ComponentType<Registration3>.Id;
            _ = ComponentType<Registration4>.Id;
            _ = ComponentType<Registration5>.Id;
            _ = ComponentType<Registration6>.Id;
            _ = ComponentType<Registration7>.Id;
            _ = ComponentType<Registration8>.Id;
            _ = ComponentType<Registration9>.Id;
            _ = ComponentType<Registration10>.Id;
            _ = ComponentType<Registration11>.Id;
            _ = ComponentType<Registration12>.Id;
            _ = ComponentType<Registration13>.Id;
            _ = ComponentType<Registration14>.Id;
            _ = ComponentType<Registration15>.Id;
            _ = ComponentType<Registration16>.Id;
            _ = ComponentType<Registration17>.Id;
            _ = ComponentType<Registration18>.Id;
            _ = ComponentType<Registration19>.Id;
            _ = ComponentType<Registration20>.Id;
            _ = ComponentType<Registration21>.Id;
            _ = ComponentType<Registration22>.Id;
            _ = ComponentType<Registration23>.Id;
            _ = ComponentType<Registration24>.Id;
            _ = ComponentType<Registration25>.Id;
            _ = ComponentType<Registration26>.Id;
            _ = ComponentType<Registration27>.Id;
            _ = ComponentType<Registration28>.Id;
            _ = ComponentType<Registration29>.Id;
            _ = ComponentType<Registration30>.Id;
            _ = ComponentType<Registration31>.Id;
            _ = ComponentType<Registration32>.Id;
            _ = ComponentType<Registration33>.Id;
            _ = ComponentType<Registration34>.Id;
            _ = ComponentType<Registration35>.Id;
            _ = ComponentType<Registration36>.Id;
            _ = ComponentType<Registration37>.Id;
            _ = ComponentType<Registration38>.Id;
            _ = ComponentType<Registration39>.Id;
            _ = ComponentType<Registration40>.Id;
            _ = ComponentType<Registration41>.Id;
            _ = ComponentType<Registration42>.Id;
            _ = ComponentType<Registration43>.Id;
            _ = ComponentType<Registration44>.Id;
            _ = ComponentType<Registration45>.Id;
            _ = ComponentType<Registration46>.Id;
            _ = ComponentType<Registration47>.Id;
            _ = ComponentType<Registration48>.Id;
            _ = ComponentType<Registration49>.Id;
            _ = ComponentType<Registration50>.Id;
            _ = ComponentType<Registration51>.Id;
            _ = ComponentType<Registration52>.Id;
            _ = ComponentType<Registration53>.Id;
            _ = ComponentType<Registration54>.Id;
            _ = ComponentType<Registration55>.Id;
            _ = ComponentType<Registration56>.Id;
            _ = ComponentType<Registration57>.Id;
            _ = ComponentType<Registration58>.Id;
            _ = ComponentType<Registration59>.Id;
            _ = ComponentType<Registration60>.Id;
            _ = ComponentType<Registration61>.Id;
            _ = ComponentType<Registration62>.Id;
            _ = ComponentType<Registration63>.Id;
            _ = ComponentType<Registration64>.Id;
            _ = ComponentType<Registration65>.Id;
            _ = ComponentType<Registration66>.Id;
            _ = ComponentType<Registration67>.Id;
            _ = ComponentType<Registration68>.Id;
            _ = ComponentType<Registration69>.Id;
            _ = ComponentType<Registration70>.Id;
            _ = ComponentType<Registration71>.Id;
            _ = ComponentType<Registration72>.Id;
            _ = ComponentType<Registration73>.Id;
            _ = ComponentType<Registration74>.Id;
            _ = ComponentType<Registration75>.Id;
            _ = ComponentType<Registration76>.Id;
            _ = ComponentType<Registration77>.Id;
            _ = ComponentType<Registration78>.Id;
            _ = ComponentType<Registration79>.Id;
            _ = ComponentType<Registration80>.Id;
            _ = ComponentType<Registration81>.Id;
            _ = ComponentType<Registration82>.Id;
            _ = ComponentType<Registration83>.Id;
            _ = ComponentType<Registration84>.Id;
            _ = ComponentType<Registration85>.Id;
            _ = ComponentType<Registration86>.Id;
            _ = ComponentType<Registration87>.Id;
            _ = ComponentType<Registration88>.Id;
            _ = ComponentType<Registration89>.Id;
            _ = ComponentType<Registration90>.Id;
            _ = ComponentType<Registration91>.Id;
            _ = ComponentType<Registration92>.Id;
            _ = ComponentType<Registration93>.Id;
            _ = ComponentType<Registration94>.Id;
            _ = ComponentType<Registration95>.Id;
            _ = ComponentType<Registration96>.Id;
            _ = ComponentType<Registration97>.Id;
            _ = ComponentType<Registration98>.Id;
            _ = ComponentType<Registration99>.Id;
            _ = ComponentType<Registration100>.Id;
            _ = ComponentType<Registration101>.Id;
            _ = ComponentType<Registration102>.Id;
            _ = ComponentType<Registration103>.Id;
            _ = ComponentType<Registration104>.Id;
            _ = ComponentType<Registration105>.Id;
            _ = ComponentType<Registration106>.Id;
            _ = ComponentType<Registration107>.Id;
            _ = ComponentType<Registration108>.Id;
            _ = ComponentType<Registration109>.Id;
            _ = ComponentType<Registration110>.Id;
            _ = ComponentType<Registration111>.Id;
            _ = ComponentType<Registration112>.Id;
            _ = ComponentType<Registration113>.Id;
            _ = ComponentType<Registration114>.Id;
            _ = ComponentType<Registration115>.Id;
            _ = ComponentType<Registration116>.Id;
            _ = ComponentType<Registration117>.Id;
            _ = ComponentType<Registration118>.Id;
            _ = ComponentType<Registration119>.Id;
            _ = ComponentType<Registration120>.Id;
            _ = ComponentType<Registration121>.Id;
            _ = ComponentType<Registration122>.Id;
            _ = ComponentType<Registration123>.Id;
            _ = ComponentType<Registration124>.Id;
            _ = ComponentType<Registration125>.Id;
            _ = ComponentType<Registration126>.Id;
            _ = ComponentType<Registration127>.Id;
            _ = ComponentType<Registration128>.Id;
            _ = ComponentType<Registration129>.Id;
            _ = ComponentType<Registration130>.Id;
            _ = ComponentType<Registration131>.Id;
            _ = ComponentType<Registration132>.Id;
            _ = ComponentType<Registration133>.Id;
            _ = ComponentType<Registration134>.Id;
            _ = ComponentType<Registration135>.Id;
            _ = ComponentType<Registration136>.Id;
            _ = ComponentType<Registration137>.Id;
            _ = ComponentType<Registration138>.Id;
            _ = ComponentType<Registration139>.Id;
            _ = ComponentType<Registration140>.Id;
            _ = ComponentType<Registration141>.Id;
            _ = ComponentType<Registration142>.Id;
            _ = ComponentType<Registration143>.Id;
            _ = ComponentType<Registration144>.Id;
            _ = ComponentType<Registration145>.Id;
            _ = ComponentType<Registration146>.Id;
            _ = ComponentType<Registration147>.Id;
            _ = ComponentType<Registration148>.Id;
            _ = ComponentType<Registration149>.Id;
            _ = ComponentType<Registration150>.Id;
            _ = ComponentType<Registration151>.Id;
            _ = ComponentType<Registration152>.Id;
            _ = ComponentType<Registration153>.Id;
            _ = ComponentType<Registration154>.Id;
            _ = ComponentType<Registration155>.Id;
            _ = ComponentType<Registration156>.Id;
            _ = ComponentType<Registration157>.Id;
            _ = ComponentType<Registration158>.Id;
            _ = ComponentType<Registration159>.Id;
            _ = ComponentType<Registration160>.Id;
            _ = ComponentType<Registration161>.Id;
            _ = ComponentType<Registration162>.Id;
            _ = ComponentType<Registration163>.Id;
            _ = ComponentType<Registration164>.Id;
            _ = ComponentType<Registration165>.Id;
            _ = ComponentType<Registration166>.Id;
            _ = ComponentType<Registration167>.Id;
            _ = ComponentType<Registration168>.Id;
            _ = ComponentType<Registration169>.Id;
            _ = ComponentType<Registration170>.Id;
            _ = ComponentType<Registration171>.Id;
            _ = ComponentType<Registration172>.Id;
            _ = ComponentType<Registration173>.Id;
            _ = ComponentType<Registration174>.Id;
            _ = ComponentType<Registration175>.Id;
            _ = ComponentType<Registration176>.Id;
            _ = ComponentType<Registration177>.Id;
            _ = ComponentType<Registration178>.Id;
            _ = ComponentType<Registration179>.Id;
            _ = ComponentType<Registration180>.Id;
            _ = ComponentType<Registration181>.Id;
            _ = ComponentType<Registration182>.Id;
            _ = ComponentType<Registration183>.Id;
            _ = ComponentType<Registration184>.Id;
            _ = ComponentType<Registration185>.Id;
            _ = ComponentType<Registration186>.Id;
            _ = ComponentType<Registration187>.Id;
            _ = ComponentType<Registration188>.Id;
            _ = ComponentType<Registration189>.Id;
            _ = ComponentType<Registration190>.Id;
            _ = ComponentType<Registration191>.Id;
            _ = ComponentType<Registration192>.Id;
            _ = ComponentType<Registration193>.Id;
            _ = ComponentType<Registration194>.Id;
            _ = ComponentType<Registration195>.Id;
            _ = ComponentType<Registration196>.Id;
            _ = ComponentType<Registration197>.Id;
            _ = ComponentType<Registration198>.Id;
            _ = ComponentType<Registration199>.Id;
            _ = ComponentType<Registration200>.Id;
            _ = ComponentType<Registration201>.Id;
            _ = ComponentType<Registration202>.Id;
            _ = ComponentType<Registration203>.Id;
            _ = ComponentType<Registration204>.Id;
            _ = ComponentType<Registration205>.Id;
            _ = ComponentType<Registration206>.Id;
            _ = ComponentType<Registration207>.Id;
            _ = ComponentType<Registration208>.Id;
            _ = ComponentType<Registration209>.Id;
            _ = ComponentType<Registration210>.Id;
            _ = ComponentType<Registration211>.Id;
            _ = ComponentType<Registration212>.Id;
            _ = ComponentType<Registration213>.Id;
            _ = ComponentType<Registration214>.Id;
            _ = ComponentType<Registration215>.Id;
            _ = ComponentType<Registration216>.Id;
            _ = ComponentType<Registration217>.Id;
            _ = ComponentType<Registration218>.Id;
            _ = ComponentType<Registration219>.Id;
            _ = ComponentType<Registration220>.Id;
            _ = ComponentType<Registration221>.Id;
            _ = ComponentType<Registration222>.Id;
            _ = ComponentType<Registration223>.Id;
            _ = ComponentType<Registration224>.Id;
            _ = ComponentType<Registration225>.Id;
            _ = ComponentType<Registration226>.Id;
            _ = ComponentType<Registration227>.Id;
            _ = ComponentType<Registration228>.Id;
            _ = ComponentType<Registration229>.Id;
            _ = ComponentType<Registration230>.Id;
            _ = ComponentType<Registration231>.Id;
            _ = ComponentType<Registration232>.Id;
            _ = ComponentType<Registration233>.Id;
            _ = ComponentType<Registration234>.Id;
            _ = ComponentType<Registration235>.Id;
            _ = ComponentType<Registration236>.Id;
            _ = ComponentType<Registration237>.Id;
            _ = ComponentType<Registration238>.Id;
            _ = ComponentType<Registration239>.Id;
            _ = ComponentType<Registration240>.Id;
            _ = ComponentType<Registration241>.Id;
            _ = ComponentType<Registration242>.Id;
            _ = ComponentType<Registration243>.Id;
            _ = ComponentType<Registration244>.Id;
            _ = ComponentType<Registration245>.Id;
            _ = ComponentType<Registration246>.Id;
            _ = ComponentType<Registration247>.Id;
            _ = ComponentType<Registration248>.Id;
            _ = ComponentType<Registration249>.Id;
            _ = ComponentType<Registration250>.Id;
            _ = ComponentType<Registration251>.Id;
            _ = ComponentType<Registration252>.Id;
            _ = ComponentType<Registration253>.Id;
            _ = ComponentType<Registration254>.Id;
            _ = ComponentType<Registration255>.Id;
            _ = ComponentType<Registration256>.Id;
            _ = ComponentType<Registration257>.Id;
            _ = ComponentType<Registration258>.Id;
            _ = ComponentType<Registration259>.Id;
            _ = ComponentType<Registration260>.Id;
            _ = ComponentType<Registration261>.Id;
            _ = ComponentType<Registration262>.Id;
            _ = ComponentType<Registration263>.Id;
            _ = ComponentType<Registration264>.Id;
            _ = ComponentType<Registration265>.Id;
            _ = ComponentType<Registration266>.Id;
            _ = ComponentType<Registration267>.Id;
            _ = ComponentType<Registration268>.Id;
            _ = ComponentType<Registration269>.Id;
            _ = ComponentType<Registration270>.Id;
            _ = ComponentType<Registration271>.Id;
            _ = ComponentType<Registration272>.Id;
            _ = ComponentType<Registration273>.Id;
            _ = ComponentType<Registration274>.Id;
            _ = ComponentType<Registration275>.Id;
            _ = ComponentType<Registration276>.Id;
            _ = ComponentType<Registration277>.Id;
            _ = ComponentType<Registration278>.Id;
            _ = ComponentType<Registration279>.Id;
            _ = ComponentType<Registration280>.Id;
            _ = ComponentType<Registration281>.Id;
            _ = ComponentType<Registration282>.Id;
            _ = ComponentType<Registration283>.Id;
            _ = ComponentType<Registration284>.Id;
            _ = ComponentType<Registration285>.Id;
            _ = ComponentType<Registration286>.Id;
            _ = ComponentType<Registration287>.Id;
            _ = ComponentType<Registration288>.Id;
            _ = ComponentType<Registration289>.Id;
            _ = ComponentType<Registration290>.Id;
            _ = ComponentType<Registration291>.Id;
            _ = ComponentType<Registration292>.Id;
            _ = ComponentType<Registration293>.Id;
            _ = ComponentType<Registration294>.Id;
            _ = ComponentType<Registration295>.Id;
            _ = ComponentType<Registration296>.Id;
            _ = ComponentType<Registration297>.Id;
            _ = ComponentType<Registration298>.Id;
            _ = ComponentType<Registration299>.Id;
        var overflowId = ComponentType<OverflowLookupComponent>.Id;
        if (overflowId < 256)
            throw new InvalidOperationException($"Expected overflow TypeId >= 256, got {overflowId}.");

        _world = new World(2);
        _directEntity = _world.Spawn();
        _overflowEntity = _world.Spawn();
        _directEntity.Add(new DirectLookupComponent { Value = 1 });
        _overflowEntity.Add(new OverflowLookupComponent { Value = 1 });
    }

    [GlobalCleanup]
    public void Cleanup() => _world.Dispose();

    [Benchmark(Baseline = true)]
    public void DirectLookup_GetSet()
    {
        var data = _directEntity.Data;
        data.Get<DirectLookupComponent>().Value++;
    }

    [Benchmark]
    public void OverflowLookup_GetSet()
    {
        var data = _overflowEntity.Data;
        data.Get<OverflowLookupComponent>().Value++;
    }

    [Benchmark]
    public bool DirectLookup_Has() => _directEntity.Has<DirectLookupComponent>();

    [Benchmark]
    public bool OverflowLookup_Has() => _overflowEntity.Has<OverflowLookupComponent>();
}

    public struct Registration0 { public int Value; }
    public struct Registration1 { public int Value; }
    public struct Registration2 { public int Value; }
    public struct Registration3 { public int Value; }
    public struct Registration4 { public int Value; }
    public struct Registration5 { public int Value; }
    public struct Registration6 { public int Value; }
    public struct Registration7 { public int Value; }
    public struct Registration8 { public int Value; }
    public struct Registration9 { public int Value; }
    public struct Registration10 { public int Value; }
    public struct Registration11 { public int Value; }
    public struct Registration12 { public int Value; }
    public struct Registration13 { public int Value; }
    public struct Registration14 { public int Value; }
    public struct Registration15 { public int Value; }
    public struct Registration16 { public int Value; }
    public struct Registration17 { public int Value; }
    public struct Registration18 { public int Value; }
    public struct Registration19 { public int Value; }
    public struct Registration20 { public int Value; }
    public struct Registration21 { public int Value; }
    public struct Registration22 { public int Value; }
    public struct Registration23 { public int Value; }
    public struct Registration24 { public int Value; }
    public struct Registration25 { public int Value; }
    public struct Registration26 { public int Value; }
    public struct Registration27 { public int Value; }
    public struct Registration28 { public int Value; }
    public struct Registration29 { public int Value; }
    public struct Registration30 { public int Value; }
    public struct Registration31 { public int Value; }
    public struct Registration32 { public int Value; }
    public struct Registration33 { public int Value; }
    public struct Registration34 { public int Value; }
    public struct Registration35 { public int Value; }
    public struct Registration36 { public int Value; }
    public struct Registration37 { public int Value; }
    public struct Registration38 { public int Value; }
    public struct Registration39 { public int Value; }
    public struct Registration40 { public int Value; }
    public struct Registration41 { public int Value; }
    public struct Registration42 { public int Value; }
    public struct Registration43 { public int Value; }
    public struct Registration44 { public int Value; }
    public struct Registration45 { public int Value; }
    public struct Registration46 { public int Value; }
    public struct Registration47 { public int Value; }
    public struct Registration48 { public int Value; }
    public struct Registration49 { public int Value; }
    public struct Registration50 { public int Value; }
    public struct Registration51 { public int Value; }
    public struct Registration52 { public int Value; }
    public struct Registration53 { public int Value; }
    public struct Registration54 { public int Value; }
    public struct Registration55 { public int Value; }
    public struct Registration56 { public int Value; }
    public struct Registration57 { public int Value; }
    public struct Registration58 { public int Value; }
    public struct Registration59 { public int Value; }
    public struct Registration60 { public int Value; }
    public struct Registration61 { public int Value; }
    public struct Registration62 { public int Value; }
    public struct Registration63 { public int Value; }
    public struct Registration64 { public int Value; }
    public struct Registration65 { public int Value; }
    public struct Registration66 { public int Value; }
    public struct Registration67 { public int Value; }
    public struct Registration68 { public int Value; }
    public struct Registration69 { public int Value; }
    public struct Registration70 { public int Value; }
    public struct Registration71 { public int Value; }
    public struct Registration72 { public int Value; }
    public struct Registration73 { public int Value; }
    public struct Registration74 { public int Value; }
    public struct Registration75 { public int Value; }
    public struct Registration76 { public int Value; }
    public struct Registration77 { public int Value; }
    public struct Registration78 { public int Value; }
    public struct Registration79 { public int Value; }
    public struct Registration80 { public int Value; }
    public struct Registration81 { public int Value; }
    public struct Registration82 { public int Value; }
    public struct Registration83 { public int Value; }
    public struct Registration84 { public int Value; }
    public struct Registration85 { public int Value; }
    public struct Registration86 { public int Value; }
    public struct Registration87 { public int Value; }
    public struct Registration88 { public int Value; }
    public struct Registration89 { public int Value; }
    public struct Registration90 { public int Value; }
    public struct Registration91 { public int Value; }
    public struct Registration92 { public int Value; }
    public struct Registration93 { public int Value; }
    public struct Registration94 { public int Value; }
    public struct Registration95 { public int Value; }
    public struct Registration96 { public int Value; }
    public struct Registration97 { public int Value; }
    public struct Registration98 { public int Value; }
    public struct Registration99 { public int Value; }
    public struct Registration100 { public int Value; }
    public struct Registration101 { public int Value; }
    public struct Registration102 { public int Value; }
    public struct Registration103 { public int Value; }
    public struct Registration104 { public int Value; }
    public struct Registration105 { public int Value; }
    public struct Registration106 { public int Value; }
    public struct Registration107 { public int Value; }
    public struct Registration108 { public int Value; }
    public struct Registration109 { public int Value; }
    public struct Registration110 { public int Value; }
    public struct Registration111 { public int Value; }
    public struct Registration112 { public int Value; }
    public struct Registration113 { public int Value; }
    public struct Registration114 { public int Value; }
    public struct Registration115 { public int Value; }
    public struct Registration116 { public int Value; }
    public struct Registration117 { public int Value; }
    public struct Registration118 { public int Value; }
    public struct Registration119 { public int Value; }
    public struct Registration120 { public int Value; }
    public struct Registration121 { public int Value; }
    public struct Registration122 { public int Value; }
    public struct Registration123 { public int Value; }
    public struct Registration124 { public int Value; }
    public struct Registration125 { public int Value; }
    public struct Registration126 { public int Value; }
    public struct Registration127 { public int Value; }
    public struct Registration128 { public int Value; }
    public struct Registration129 { public int Value; }
    public struct Registration130 { public int Value; }
    public struct Registration131 { public int Value; }
    public struct Registration132 { public int Value; }
    public struct Registration133 { public int Value; }
    public struct Registration134 { public int Value; }
    public struct Registration135 { public int Value; }
    public struct Registration136 { public int Value; }
    public struct Registration137 { public int Value; }
    public struct Registration138 { public int Value; }
    public struct Registration139 { public int Value; }
    public struct Registration140 { public int Value; }
    public struct Registration141 { public int Value; }
    public struct Registration142 { public int Value; }
    public struct Registration143 { public int Value; }
    public struct Registration144 { public int Value; }
    public struct Registration145 { public int Value; }
    public struct Registration146 { public int Value; }
    public struct Registration147 { public int Value; }
    public struct Registration148 { public int Value; }
    public struct Registration149 { public int Value; }
    public struct Registration150 { public int Value; }
    public struct Registration151 { public int Value; }
    public struct Registration152 { public int Value; }
    public struct Registration153 { public int Value; }
    public struct Registration154 { public int Value; }
    public struct Registration155 { public int Value; }
    public struct Registration156 { public int Value; }
    public struct Registration157 { public int Value; }
    public struct Registration158 { public int Value; }
    public struct Registration159 { public int Value; }
    public struct Registration160 { public int Value; }
    public struct Registration161 { public int Value; }
    public struct Registration162 { public int Value; }
    public struct Registration163 { public int Value; }
    public struct Registration164 { public int Value; }
    public struct Registration165 { public int Value; }
    public struct Registration166 { public int Value; }
    public struct Registration167 { public int Value; }
    public struct Registration168 { public int Value; }
    public struct Registration169 { public int Value; }
    public struct Registration170 { public int Value; }
    public struct Registration171 { public int Value; }
    public struct Registration172 { public int Value; }
    public struct Registration173 { public int Value; }
    public struct Registration174 { public int Value; }
    public struct Registration175 { public int Value; }
    public struct Registration176 { public int Value; }
    public struct Registration177 { public int Value; }
    public struct Registration178 { public int Value; }
    public struct Registration179 { public int Value; }
    public struct Registration180 { public int Value; }
    public struct Registration181 { public int Value; }
    public struct Registration182 { public int Value; }
    public struct Registration183 { public int Value; }
    public struct Registration184 { public int Value; }
    public struct Registration185 { public int Value; }
    public struct Registration186 { public int Value; }
    public struct Registration187 { public int Value; }
    public struct Registration188 { public int Value; }
    public struct Registration189 { public int Value; }
    public struct Registration190 { public int Value; }
    public struct Registration191 { public int Value; }
    public struct Registration192 { public int Value; }
    public struct Registration193 { public int Value; }
    public struct Registration194 { public int Value; }
    public struct Registration195 { public int Value; }
    public struct Registration196 { public int Value; }
    public struct Registration197 { public int Value; }
    public struct Registration198 { public int Value; }
    public struct Registration199 { public int Value; }
    public struct Registration200 { public int Value; }
    public struct Registration201 { public int Value; }
    public struct Registration202 { public int Value; }
    public struct Registration203 { public int Value; }
    public struct Registration204 { public int Value; }
    public struct Registration205 { public int Value; }
    public struct Registration206 { public int Value; }
    public struct Registration207 { public int Value; }
    public struct Registration208 { public int Value; }
    public struct Registration209 { public int Value; }
    public struct Registration210 { public int Value; }
    public struct Registration211 { public int Value; }
    public struct Registration212 { public int Value; }
    public struct Registration213 { public int Value; }
    public struct Registration214 { public int Value; }
    public struct Registration215 { public int Value; }
    public struct Registration216 { public int Value; }
    public struct Registration217 { public int Value; }
    public struct Registration218 { public int Value; }
    public struct Registration219 { public int Value; }
    public struct Registration220 { public int Value; }
    public struct Registration221 { public int Value; }
    public struct Registration222 { public int Value; }
    public struct Registration223 { public int Value; }
    public struct Registration224 { public int Value; }
    public struct Registration225 { public int Value; }
    public struct Registration226 { public int Value; }
    public struct Registration227 { public int Value; }
    public struct Registration228 { public int Value; }
    public struct Registration229 { public int Value; }
    public struct Registration230 { public int Value; }
    public struct Registration231 { public int Value; }
    public struct Registration232 { public int Value; }
    public struct Registration233 { public int Value; }
    public struct Registration234 { public int Value; }
    public struct Registration235 { public int Value; }
    public struct Registration236 { public int Value; }
    public struct Registration237 { public int Value; }
    public struct Registration238 { public int Value; }
    public struct Registration239 { public int Value; }
    public struct Registration240 { public int Value; }
    public struct Registration241 { public int Value; }
    public struct Registration242 { public int Value; }
    public struct Registration243 { public int Value; }
    public struct Registration244 { public int Value; }
    public struct Registration245 { public int Value; }
    public struct Registration246 { public int Value; }
    public struct Registration247 { public int Value; }
    public struct Registration248 { public int Value; }
    public struct Registration249 { public int Value; }
    public struct Registration250 { public int Value; }
    public struct Registration251 { public int Value; }
    public struct Registration252 { public int Value; }
    public struct Registration253 { public int Value; }
    public struct Registration254 { public int Value; }
    public struct Registration255 { public int Value; }
    public struct Registration256 { public int Value; }
    public struct Registration257 { public int Value; }
    public struct Registration258 { public int Value; }
    public struct Registration259 { public int Value; }
    public struct Registration260 { public int Value; }
    public struct Registration261 { public int Value; }
    public struct Registration262 { public int Value; }
    public struct Registration263 { public int Value; }
    public struct Registration264 { public int Value; }
    public struct Registration265 { public int Value; }
    public struct Registration266 { public int Value; }
    public struct Registration267 { public int Value; }
    public struct Registration268 { public int Value; }
    public struct Registration269 { public int Value; }
    public struct Registration270 { public int Value; }
    public struct Registration271 { public int Value; }
    public struct Registration272 { public int Value; }
    public struct Registration273 { public int Value; }
    public struct Registration274 { public int Value; }
    public struct Registration275 { public int Value; }
    public struct Registration276 { public int Value; }
    public struct Registration277 { public int Value; }
    public struct Registration278 { public int Value; }
    public struct Registration279 { public int Value; }
    public struct Registration280 { public int Value; }
    public struct Registration281 { public int Value; }
    public struct Registration282 { public int Value; }
    public struct Registration283 { public int Value; }
    public struct Registration284 { public int Value; }
    public struct Registration285 { public int Value; }
    public struct Registration286 { public int Value; }
    public struct Registration287 { public int Value; }
    public struct Registration288 { public int Value; }
    public struct Registration289 { public int Value; }
    public struct Registration290 { public int Value; }
    public struct Registration291 { public int Value; }
    public struct Registration292 { public int Value; }
    public struct Registration293 { public int Value; }
    public struct Registration294 { public int Value; }
    public struct Registration295 { public int Value; }
    public struct Registration296 { public int Value; }
    public struct Registration297 { public int Value; }
    public struct Registration298 { public int Value; }
    public struct Registration299 { public int Value; }
