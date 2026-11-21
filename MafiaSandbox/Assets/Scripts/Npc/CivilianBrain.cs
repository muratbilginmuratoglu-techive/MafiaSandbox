using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using MafiaSandbox.Combat;
using MafiaSandbox.Core;
using MafiaSandbox.Economy;
using MafiaSandbox.Vehicles;
using MafiaSandbox.World;

namespace MafiaSandbox.Npc
{
    /// <summary>
    /// Civilians lead small lives: they run errands (walk into a shop, browse, pay, walk out), stroll, stop to check their phone
    /// or take a call, bump into friends and chat (taking turns, nodding along), sometimes walk on together side by side, and
    /// go home when it gets late. They notice the player: a glance when he passes, a greeting, a complaint when he barges into
    /// them, unease when he stands too close, and they back off when he raises a gun. Trouble scatters them in different ways:
    /// some freeze and cover their heads, most run, some duck into a shop; afterwards a few phone the police, the brave ones
    /// gather round a body and stare. Medics (Doktor_*) walk to the wounded and treat them.
    /// </summary>
    public class CivilianBrain : NpcBrain
    {
        static readonly HashSet<CharacterBody> attended = new HashSet<CharacterBody>();
        static readonly Dictionary<Shop, int> visitors = new Dictionary<Shop, int>();
        public bool isMedic;
        [Range(0f, 1f)] public float bravery = 0.3f;

        Vector3 threat; float scared; CharacterBody patient; Vector3 noisePoint; Incident witnessed;
        float personality = -1f, social, phoneHabit, reactT, bodyMemo, greetCd, bumpCd, crowdT, gunCd, activityT, talkT;
        float chatLen = 6f, turnT, browseLen; bool speaking, chatLead, paid;
        CivilianBrain chatWith, companion, follower;
        Shop errandShop; Vector3 browseSpot, browseLook, shopOut;
        string activity = "";

        /// <summary>Walking about, shopping, chatting: the player can talk to them and nobody minds them being switched off at night.</summary>
        public bool Calm => State == "shelter" || State == "wander" || State == "wait" || State == "chat" || State == "browse" || State == "errand" || State == "enter" || State == "leave" || State == "together" || State == "look" || State == "cautious" || State == "avoid" || State == "curious";
        public bool Busy => !Calm;
        protected override bool AllowGlance => Calm || State == "watch";
        protected override float ThinkInterval => DistToPlayer < 70f ? 0.2f : 0.5f;

        protected override void OnSpawned()
        {
            if (personality < 0f) { personality = Random.value; social = Random.value; phoneHabit = Random.value * Random.value; walkSpeed = Random.Range(1.45f, 2.3f); }
            ClearErrand(); EndCompany(false); idleClip = "Idle"; scared = 0f; patient = null; pose = ArmPose.None; headTarget = null; headDown = false; nod = 0f;
            SetState("wander"); NextActivity();
        }
        protected override void OnEnable() { base.OnEnable(); if (State == "home") { SetState("wait"); stateT = -Random.Range(0.5f, 2f); idleClip = "Idle"; } }
        protected override void OnDisable() { base.OnDisable(); ClearErrand(); EndCompany(false); }

        static float Hour => DayNightCycle.I ? DayNightCycle.I.timeOfDay : 12f;
        static bool Night => Hour < 6f || Hour > 21f;
        static Atmosphere.WeatherKind Weather => Atmosphere.I ? Atmosphere.I.weather : Atmosphere.WeatherKind.Clear;
        static bool Wet => Weather != Atmosphere.WeatherKind.Clear;
        Atmosphere.WeatherKind seenWeather; bool weatherKnown;

        /// <summary>Rain or snow: get out of it, into a shop doorway or a porch, until it passes.</summary>
        bool TryShelter()
        {
            BuildingInfo best = null; float bd = 40f * 40f;
            foreach (var bi in BuildingInfo.All)
            {
                if (!bi || bi.kind == "home" || bi.kind == "police" || bi.kind == "house") continue;
                var sh = EconomyManager.I ? EconomyManager.I.ShopFor(bi) : null; if (sh && visitors.TryGetValue(sh, out var n) && n >= 3) continue;
                float d = (bi.door - transform.position).sqrMagnitude; if (d < bd) { bd = d; best = bi; }
            }
            if (!best) return false;
            Vector3 inw = best.footprint.center - best.door; inw.y = 0f; Vector3 spot = best.door + inw.normalized * Random.Range(1f, 2f) + Vector3.Cross(Vector3.up, inw.normalized) * Random.Range(-1f, 1f);
            if (!GoTo(spot, WalkPace() * 1.15f, 2.5f)) return false;
            noisePoint = best.door - inw.normalized * 3f; SetState("shelter"); activity = ""; return true;
        }

        // ================================================================== what to do next
        void NextActivity()
        {
            ClearErrand(); pose = ArmPose.None; headDown = false; headTarget = null; nod = 0f; idleClip = "Idle";
            if (follower && Random.value < 0.25f) { Bark(Barks.Pick("Right, I'm off.", "See you later!", "Catch you tomorrow.", "Take care!")); anim.PlayUpper("Wave"); EndCompany(true); }
            if (Wet && Random.value < 0.45f && TryShelter()) return;
            float h = Hour; float errandChance = h > 8f && h < 20f ? 0.42f : h > 20f && h < 23f ? 0.2f : 0.08f;
            if (Random.value < errandChance && TryErrand()) return;
            // stroll somewhere not too far: people don't cross the whole city on a whim
            var p = RandomPedNode(transform.position, Random.value < 0.7f ? 45f : 80f) + new Vector3(Random.Range(-1.2f, 1.2f), 0f, Random.Range(-1.2f, 1.2f));
            if (!GoTo(p, WalkPace())) { SetState("wait"); stateT = -2f; return; }
            SetState("wander");
            // eyes on the phone while walking (slower, head down)
            if (Wet) { headDown = Random.value < 0.6f; activity = ""; return; }   // head down against the rain, no phone out
            if (!follower && Random.value < phoneHabit * 0.7f) { activity = "text"; pose = ArmPose.PhoneText; headDown = true; agent.speed = WalkPace() * 0.8f; activityT = Random.Range(5f, 12f); }
            else activity = "";
        }
        float WalkPace() { float s = walkSpeed * (Night ? 0.92f : 1f) * (Weather == Atmosphere.WeatherKind.Rain ? 1.25f : Weather == Atmosphere.WeatherKind.Snow ? 0.9f : 1f); if (follower) s = Mathf.Min(s, follower.walkSpeed); return s; }

        bool TryErrand()
        {
            var eco = EconomyManager.I; if (!eco) return false; Shop best = null; float bd = 75f * 75f;
            foreach (var s in eco.shops)
            {
                if (!s || !s.info || s.kind == "house" || s.kind == "home" || s.kind == "police" || !s.HasLiveKeeper) continue;
                if (visitors.TryGetValue(s, out var n) && n >= 2) continue;
                float d = (s.info.door - transform.position).sqrMagnitude; if (d > bd || d < 9f) continue;
                if (Random.value < 0.35f) continue;   // not always the nearest one
                bd = d; best = s;
            }
            if (!best) return false;
            Vector3 outw = best.info.door - best.info.footprint.center; outw.y = 0f; outw = outw.sqrMagnitude > 0.01f ? outw.normalized : Vector3.forward;
            shopOut = best.info.door + outw * 1.4f;
            if (!GoTo(shopOut, WalkPace())) return false;
            errandShop = best; visitors[best] = (visitors.TryGetValue(best, out var c) ? c : 0) + 1; paid = false;
            // where to stand inside: in front of a shelf, or at the counter
            var bi = best.info; Vector3 centre = bi.footprint.center;
            if (bi.shelves != null && bi.shelves.Length > 0 && Random.value < 0.75f) { var sh = bi.shelves[Random.Range(0, bi.shelves.Length)]; Vector3 inw = centre - sh; inw.y = 0f; browseSpot = sh + inw.normalized * 0.95f; browseLook = sh; }
            else { Vector3 td = bi.door - bi.counter; td.y = 0f; browseSpot = bi.counter + td.normalized * 1.15f; browseLook = bi.counter; }
            browseSpot.y = bi.footprint.min.y;
            SetState("errand"); activity = ""; return true;
        }
        void ClearErrand() { if (errandShop) { if (visitors.TryGetValue(errandShop, out var n)) visitors[errandShop] = Mathf.Max(0, n - 1); errandShop = null; } }

        // ================================================================== company
        void StartChatWith(CivilianBrain o, bool greet)
        {
            chatLen = Random.Range(7f, 14f); chatWith = o; speaking = true; chatLead = true; turnT = Random.Range(2f, 3.5f); talkT = 0f;
            Stop(); lookAt = o.transform.position; headTarget = o.transform.position + Vector3.up * 1.55f; pose = ArmPose.None; headDown = false; SetState("chat"); activity = "";
            o.JoinChat(this, chatLen);
            if (greet) { anim.PlayUpper("Wave"); Bark(Greeting(), null, 2f); }
        }
        void JoinChat(CivilianBrain with, float len)
        {
            ClearErrand(); chatLen = len; chatWith = with; speaking = false; chatLead = false; Stop(); lookAt = with.transform.position; headTarget = with.transform.position + Vector3.up * 1.55f; pose = ArmPose.None; headDown = false; SetState("chat"); activity = "";
            if (Random.value < 0.5f) Invoke(nameof(WaveBack), 0.6f);
        }
        void WaveBack() { if (State == "chat") { anim.PlayUpper("Wave"); Bark(Barks.Pick("Hey!", "Oh, hi!", "Hello there!", "Look who it is!"), null, 1.8f); } }
        void EndChat(bool goodbye)
        {
            var o = chatWith; chatWith = null; nod = 0f; headTarget = null; idleClip = "Idle";
            if (goodbye) { anim.PlayUpper("Wave"); Bark(Barks.Pick("See you!", "Take care!", "Bye now.", "Say hi to everyone.", "Talk soon!"), null, 2f); }
            if (o && o.chatWith == this) { o.chatWith = null; o.nod = 0f; o.headTarget = null; o.idleClip = "Idle"; }
            // sometimes they head off together
            if (goodbye && o && o.Calm && !o.Dead && social > 0.5f && o.social > 0.4f && !follower && !companion && !o.follower && !o.companion && Random.value < 0.35f)
            {
                follower = o; o.companion = this; o.SetState("together"); o.headTarget = null; NextActivity(); return;
            }
            NextActivity(); if (o && o.State == "chat") o.NextActivity();
        }
        void EndCompany(bool both)
        {
            if (follower) { var f = follower; follower = null; if (f.companion == this) { f.companion = null; if (both && f.State == "together") f.NextActivity(); } }
            if (companion) { var c = companion; companion = null; if (c.follower == this) c.follower = null; }
        }

        static string Greeting() { float h = Hour; return h < 11f && h > 5f ? Barks.Pick("Morning!", "Good morning!", "Hey, morning!") : h > 18f || h < 5f ? Barks.Pick("Evening!", "Hey there!", "Oh, hi!") : Barks.Pick("Hey!", "Hi!", "Hello!", "How are you?"); }
        string SmallTalk()
        {
            var inc = NearestIncident(transform.position, 90f, 200f);
            if (inc != null && Random.value < 0.6f) return inc.kind == "body" ? Barks.Pick("Someone got killed near here.", "Did you see the body?", "This neighbourhood is going to hell.", "I'm not walking home alone tonight.") : Barks.Pick("Did you hear those gunshots?", "Gunfire, in broad daylight!", "Was that shooting? Again?");
            if (Wanted && Wanted.Level > 0 && Random.value < 0.4f) return Barks.Pick("Police everywhere today.", "Sirens all morning...", "They're looking for someone.");
            var emp = Gang.Empire.I; if (emp && emp.ZonesOwned > 0 && Random.value < 0.25f) return Barks.Pick("They say a new crew runs these streets now.", "The gangs are at war again.", "I heard somebody took over a whole block.");
            float h = Hour;
            if ((h > 21f || h < 4f) && Random.value < 0.4f) return Barks.Pick("It's getting late.", "Shouldn't be out this late.", "One more drink?", "I should get home.");
            if (h > 5f && h < 10f && Random.value < 0.3f) return Barks.Pick("I need coffee.", "Off to work...", "Up since five.");
            return Barks.Pick("Nice day, isn't it?", "How's the family?", "Prices keep going up.", "I'm thinking of moving out.", "Did you see the game last night?", "My landlord is a nightmare.", "Have you tried that new cafe?", "Work, work, work...", "I haven't slept in weeks.", "You look well!", "My cousin got a job at the bank.", "Rent's due again.", "And then he says...", "You won't believe this.");
        }
        static string Reply() => Barks.Pick("Mm-hm.", "Really?", "No way!", "Tell me about it.", "Ha!", "I know, right?", "Exactly.", "Seriously?", "Oh dear.");

        // ================================================================== reactions to the world
        protected override void OnNoise(Vector3 pos, float radius, GameObject src, bool melee)
        {
            if (src == gameObject || State == "dead" || !isActiveAndEnabled) return;
            float d = Vector3.Distance(pos, transform.position);
            if (melee && d > 10f) return;
            if (d < radius) { if (!melee) witnessed = NearestIncident(pos, 20f, 5f); Panic(pos, d < radius * 0.55f); }
            else if (!melee && d < radius * 1.8f && Calm)
            {
                headTarget = pos + Vector3.up * 1.5f;
                if (State != "chat" && State != "browse" && Random.value < bravery) { noisePoint = pos; SetState("curious"); GoTo(Vector3.Lerp(transform.position, pos, 0.5f), walkSpeed); }
                else if (State == "wander" || State == "wait") { Stop(); noisePoint = pos; lookAt = pos; SetState("look"); if (Random.value < 0.4f) Bark(Barks.Pick("What was that?", "Was that a gunshot?", "Did you hear that?"), Barks.Alarm, 2.2f); }
            }
        }

        protected override void OnHit(string zone, Vector3 point) { scared = 1f; Panic(point, false); if (Random.value < 0.7f) Bark(Barks.Pick("Aagh!", "Help me!", "I'm hit!", "Please, stop!"), Barks.Alarm, 2f); }
        public override void Honk(Vector3 carPos, Vector3 carForward)
        {
            if (State == "dead" || dummy) return;
            if (!OnRoad() && State != "flee" && State != "hide" && State != "tohide") { headTarget = carPos + Vector3.up; if (Random.value < 0.2f) Bark(Barks.Pick("Yeah, yeah!", "I'm on the pavement!", "Relax!"), null, 1.8f); return; }
            if (State == "flee" || State == "hide" || State == "tohide") base.Honk(carPos, carForward); else { base.Honk(carPos, carForward); SetState("dodge"); }
        }

        /// <summary>Danger: a timid few freeze and cover their heads first, the rest run (sometimes into a shop), screaming.</summary>
        void Panic(Vector3 from, bool close)
        {
            threat = from; scared = Mathf.Max(scared, 0.6f); if (dummy) return;
            ClearErrand(); if (chatWith) { var o = chatWith; chatWith = null; if (o.chatWith == this) o.chatWith = null; }
            EndCompany(false); headDown = false; nod = 0f; headTarget = from + Vector3.up * 1.2f;
            if (State != "flee" && State != "cower" && Random.value < 0.55f) Bark(Barks.Pick("Aaah!", "Run!", "He's shooting!", "Oh my God!", "Get down!", "Help!"), Barks.Alarm, 1.8f, false, 30f);
            if (close && personality < 0.22f && State != "cower" && State != "flee") { Stop(); SetState("cower"); idleClip = "Crouch_Idle"; pose = ArmPose.Cower; lookAt = from; return; }
            StartFlee(from);
        }

        void StartFlee(Vector3 from)
        {
            threat = from; scared = Mathf.Max(scared, 0.6f); pose = ArmPose.None; idleClip = "Idle";
            if (dummy) return;
            // a shop doorway away from the danger is the best place to hide
            var eco = EconomyManager.I;
            if (eco && Random.value < 0.4f)
            {
                Vector3 away = transform.position - from; away.y = 0f; away.Normalize(); Shop best = null; float bd = 22f * 22f;
                foreach (var s in eco.shops)
                {
                    if (!s || !s.info || s.kind == "home" || s.kind == "police") continue; Vector3 to = s.info.door - transform.position; to.y = 0f; float d = to.sqrMagnitude;
                    if (d > bd || d < 1f || Vector3.Dot(to.normalized, away) < 0.25f) continue; bd = d; best = s;
                }
                if (best) { Vector3 inside = best.info.footprint.center; inside.y = best.info.footprint.min.y; if (GoTo(inside, runSpeed * Random.Range(0.85f, 1f), 3f)) { SetState("tohide"); return; } }
            }
            var p = EscapePoint(from, 28f, false);
            if (p.HasValue && GoTo(p.Value, runSpeed * Random.Range(0.85f, 1f))) SetState("flee"); else EnterHide();
        }

        void EnterHide() { Stop(); SetState("hide"); idleClip = "Crouch_Idle"; lookAt = threat; headTarget = threat + Vector3.up * 1.2f; pose = ArmPose.Cower; }

        /// <summary>Everyday reactions, a few times a second: cars, bodies, a gun, the player barging in or hovering, friends passing by.</summary>
        void React(float dt)
        {
            if (dummy || isMedic && State.StartsWith("medic")) return;
            reactT -= dt; bodyMemo -= dt; greetCd -= dt; bumpCd -= dt; gunCd -= dt; if (reactT > 0f) return; reactT = Random.Range(0.25f, 0.4f);
            var pos = transform.position;
            // 1. cars: get out of the way (and tell off reckless drivers)
            if (State != "flee" && State != "tohide" && State != "hide" && State != "dodge" && State != "cower" && OnRoad())   // on the pavement they simply stand: nobody throws themselves into the road
                foreach (var v in VehicleController.All)
                {
                    if (!v || Mathf.Abs(v.Speed) < 2.5f) continue; Vector3 rel = pos - v.transform.position; rel.y = 0f; float d = rel.magnitude; if (d > 11f) continue;
                    Vector3 dir = v.transform.forward * Mathf.Sign(v.Speed); if (Vector3.Dot(dir, rel) < 0f) continue;       // already behind it
                    float lat = Vector3.Dot(v.transform.right, rel); if (Mathf.Abs(lat) > 2.6f) continue;                     // not in its lane
                    Vector3 side = v.transform.right * (lat >= 0f ? 1f : -1f);
                    bool fast = Mathf.Abs(v.Speed) > 9f;
                    Vector3 dt2 = pos + side * (fast ? 3.6f : 2.4f) + dir * 0.5f; if (OffRoadPoint(dt2, 12f, out var safeP)) dt2 = safeP;
                    if (GoTo(dt2, fast ? runSpeed : walkSpeed * 1.5f)) { ClearErrand(); SetState("dodge"); scared = Mathf.Max(scared, fast ? 0.4f : 0f); headTarget = v.transform.position + Vector3.up; if (fast && Random.value < 0.6f) Bark(Barks.Pick("Whoa!", "Watch it!", "Are you crazy?!", "Slow down!"), Barks.Alarm, 1.8f); }
                    return;
                }
            if (!Calm && State != "watch") return;
            var pc = Player; bool pIn = PlayerInVehicle; float dp = DistToPlayer;
            // 2. dead bodies nearby: the timid run, the brave gather round (not too close) and stare
            if (bodyMemo <= 0f)
                foreach (var b in CharacterBody.All)
                {
                    if (!b || !b.Dead || b == body || (b.transform.position - pos).sqrMagnitude > 14f * 14f) continue;
                    if (!Perception.Clear(Eye, b.ChestPos)) continue;
                    bodyMemo = 30f; threat = b.transform.position; witnessed = NearestIncident(b.transform.position, 6f, 600f) ?? AddIncident(b.transform.position, "body", false);
                    ClearErrand(); EndCompany(false);
                    if (personality < 0.5f || witnessed.watchers >= 5) { StartFlee(b.transform.position); Bark(Barks.Pick("Oh God, is that...?", "Somebody's dead!", "I'm getting out of here."), Barks.Alarm, 2.2f); }
                    else
                    {
                        witnessed.watchers++;
                        Vector3 from = pos - b.transform.position; from.y = 0f; from = from.sqrMagnitude > 0.01f ? from.normalized : Random.insideUnitSphere; from.y = 0f; from.Normalize();
                        Vector3 spot = b.transform.position + Quaternion.Euler(0f, Random.Range(-40f, 40f), 0f) * from * Random.Range(4.5f, 7f);
                        GoTo(spot, walkSpeed * 0.9f); SetState("gather"); headTarget = b.ChestPos; noisePoint = b.transform.position;
                    }
                    return;
                }
            if (pc && !pIn && PlayerBody && !PlayerBody.Dead)
            {
                bool seen = dp < 16f && Perception.Clear(Eye, pc.transform.position + Vector3.up * 1.4f);
                // 3. a gun pointed at me: hands up (or run); a gun raised nearby: back away from it
                if (seen && PlayerAimingAtMe(12f))
                {
                    ClearErrand(); EndCompany(false); if (chatWith) EndChatQuiet();
                    if (personality < 0.38f || scared > 0.5f) { StartFlee(pc.transform.position); Bark(Barks.Pick("No no no!", "Don't shoot!", "Help!"), Barks.Alarm, 1.8f); }
                    else { Stop(); lookAt = pc.transform.position; headTarget = pc.transform.position + Vector3.up * 1.5f; idleClip = "Idle"; pose = ArmPose.HandsUp; SetState("handsup"); Bark(Barks.Pick("Don't shoot!", "Please, I have kids!", "Take my wallet, just don't shoot!", "Easy... easy..."), Barks.Alarm, 2.4f); }
                    return;
                }
                if (seen && gunCd <= 0f && PlayerGunUp && dp < 15f)
                {
                    gunCd = 8f; ClearErrand(); EndCompany(false); if (chatWith) EndChatQuiet();
                    if (personality < 0.42f) { StartFlee(pc.transform.position); Bark(Barks.Pick("He's got a gun!", "Gun! Gun!", "Run!"), Barks.Alarm, 2f); }
                    else { var p = EscapePoint(pc.transform.position, 14f, true); if (p.HasValue && GoTo(p.Value, walkSpeed * 1.7f)) { SetState("avoid"); headTarget = pc.transform.position + Vector3.up * 1.5f; Bark(Barks.Pick("Whoa, whoa...", "Put that thing away!", "I don't want any trouble."), Barks.Alarm, 2.2f); } }
                    return;
                }
                // 4. the player barges into me
                if (bumpCd <= 0f && dp < 0.95f && pc.Moving && pc.Velocity.magnitude > 2.2f)
                {
                    Vector3 to = pos - pc.transform.position; to.y = 0f;
                    if (Vector3.Dot(pc.Velocity.normalized, to.normalized) > 0.4f)
                    {
                        bumpCd = 5f; Stat("civ.bumped"); anim.PlayUpper(Vector3.Dot(transform.forward, to) > 0f ? "Hit_Back" : "Hit_Front");
                        Vector3 step = pos + to.normalized * 1.1f; if (NavMesh.SamplePosition(step, out var sh, 1f, NavMesh.AllAreas)) { agent.Warp(Vector3.Lerp(pos, sh.position, 0.35f)); }
                        headTarget = pc.transform.position + Vector3.up * 1.5f; glanceHold = 2.5f;
                        Bark(Barks.Pick("Hey! Watch it!", "Excuse you!", "Ow! Look where you're going!", "What's your problem?", "Rude..."), null, 2.2f);
                        return;
                    }
                }
                // 5. the player hovering right in my face
                if ((State == "wait" || State == "chat" || State == "browse") && dp < 1.5f && !pc.Moving) { crowdT += 0.3f; if (crowdT > 3f && crowdT < 3.35f) { headTarget = pc.transform.position + Vector3.up * 1.5f; Bark(Barks.Pick("Can I help you?", "Do I know you?", "Personal space, buddy.", "What?"), null, 2.2f); } else if (crowdT > 7f) { crowdT = 0f; headTarget = null; if (chatWith) EndChatQuiet(); var p = EscapePoint(pc.transform.position, 8f, false); if (p.HasValue && GoTo(p.Value, walkSpeed * 1.2f)) { SetState("cautious"); Bark(Barks.Pick("Weirdo...", "Okay, I'm leaving.", "Creep."), null, 2f); } } }
                else crowdT = Mathf.Max(0f, crowdT - 0.3f);
                // 6. a greeting for the player when he passes close
                if (greetCd <= 0f && seen && dp < 2.6f && (State == "wander" || State == "wait") && Random.value < 0.3f)
                {
                    greetCd = 50f; Stat("civ.greetPlayer"); headTarget = pc.transform.position + Vector3.up * 1.5f; glanceHold = 1.8f;
                    int rank = Gang.Respect.Rank; bool wanted = Wanted && Wanted.Level > 0;
                    Bark(wanted ? Barks.Pick("Stay away from me.", "I didn't see anything...") : rank >= 3 ? Barks.Pick("Good day, sir.", "Boss.", "Mr... sir. Hello.") : Greeting(), null, 2f);
                    if (wanted || rank >= 3) { Vector3 side = Vector3.Cross(Vector3.up, pc.transform.position - pos).normalized; GoTo(pos + side * 1.5f, walkSpeed); }
                    return;
                }
            }
            // 7. neighbours: a nod or a hello in passing, sometimes a proper chat
            if ((State == "wait" || State == "wander") && greetCd <= 0f && !follower && !companion)
                foreach (var o in Civs())
                {
                    if (!o || o == this || o.Dead || !(o.State == "wait" || o.State == "wander") || o.follower || o.companion) continue; Vector3 d = o.transform.position - pos; if (d.sqrMagnitude > 3.2f * 3.2f) continue;
                    greetCd = Random.Range(15f, 30f); o.greetCd = greetCd;
                    if (social > 0.35f && o.social > 0.3f && Random.value < (0.18f + social * 0.25f) * (Wet ? 0.3f : 1f)) { StartChatWith(o, true); return; }
                    if (Random.value < 0.35f) { Stat("civ.nod"); headTarget = o.transform.position + Vector3.up * 1.5f; glanceHold = 1.5f; if (Random.value < 0.5f) Bark(Barks.Pick("Hi.", "Hey.", "Morning.", "Evening."), null, 1.5f); }
                    break;
                }
        }
        float glanceHold; int offroadTries;
        void EndChatQuiet() { var o = chatWith; chatWith = null; nod = 0f; if (o && o.chatWith == this) { o.chatWith = null; o.nod = 0f; o.headTarget = null; o.idleClip = "Idle"; if (o.State == "chat") o.NextActivity(); } }
        static CivilianBrain[] civCache; static float civCacheT;
        static CivilianBrain[] Civs() { if (civCache == null || Time.time - civCacheT > 2f) { civCache = FindObjectsByType<CivilianBrain>(FindObjectsSortMode.None); civCacheT = Time.time; } return civCache; }

        /// <summary>Night falls: walk to a house door and go inside (NightLife switches the person off there, and back on in the morning).</summary>
        public void GoHome()
        {
            if (State == "home" || isMedic || !Calm) return;
            ClearErrand(); EndCompany(false); if (chatWith) EndChatQuiet(); pose = ArmPose.None; headDown = false;
            BuildingInfo best = null; float bd = 90f * 90f;
            foreach (var bi in BuildingInfo.All) { if (!bi || bi.kind != "house") continue; float d = (bi.door - transform.position).sqrMagnitude; if (d < bd) { bd = d; best = bi; } }
            Vector3 target = best ? best.door : RandomPedNode(transform.position, 120f);
            if (GoTo(target, walkSpeed * 1.1f, 3f)) { SetState("home"); if (Random.value < 0.3f) Bark(Barks.Pick("Time to head home.", "Long day...", "Bed, finally."), null, 2f); }
        }

        // ================================================================== the state machine
        protected override void Think()
        {
            float dt = thinkDt;
            if (!weatherKnown) { weatherKnown = true; seenWeather = Weather; }
            else if (seenWeather != Weather)
            {
                seenWeather = Weather;
                if (Calm && CamDist(transform.position) < 24f && Random.value < 0.25f) Bark(Weather == Atmosphere.WeatherKind.Rain ? Barks.Pick("Ugh, rain!", "Great, no umbrella.", "Here it comes...") : Weather == Atmosphere.WeatherKind.Snow ? Barks.Pick("Snow! Brr...", "It's freezing!", "Look, snow!") : Barks.Pick("Sun's out!", "About time."), null, 2f);
                if (Wet && (State == "wait" || State == "chat" || (State == "wander" && Random.value < 0.5f))) { if (chatWith) EndChatQuiet(); if (!TryShelter()) NextActivity(); }
            }
            if (glanceHold > 0f) { glanceHold -= dt; if (glanceHold <= 0f && Calm && State != "chat" && State != "browse" && State != "together") headTarget = null; }
            switch (State)
            {
                case "dodge":
                    if (Arrived(0.4f) || stateT > 1.6f) { SetState("wait"); stateT = -Random.Range(0.8f, 1.8f); Stop(); headTarget = null; }
                    break;
                case "wander":
                    if (activity == "text") { activityT -= dt; if (activityT <= 0f) { activity = ""; pose = ArmPose.None; headDown = false; agent.speed = WalkPace(); } }
                    if (follower && stateT % 9f < dt && Random.value < 0.6f && CamDist(transform.position) < 22f) { Bark(SmallTalk(), null, 2.6f); headTarget = follower.transform.position + Vector3.up * 1.5f; glanceHold = 2f; }
                    if (Arrived()) StartIdle();
                    else if (stateT > 50f) NextActivity();
                    break;
                case "wait":
                    if (stateT > 0f) { pose = ArmPose.None; headDown = false; idleClip = "Idle"; NextActivity(); }
                    else if (activity == "call") { talkT -= dt; if (talkT <= 0f) { talkT = Random.Range(2.5f, 4.5f); Bark(Barks.Pick("Yeah... yeah.", "No, I told him already.", "I'll be there in ten.", "Are you serious?", "Love you too.", "Did you get the milk?", "Hold on, I'm outside."), null, 2.6f, false, 16f); } }
                    break;
                case "chat":
                    if (!chatWith || chatWith.Dead || chatWith.chatWith != this || chatWith.State != "chat") { chatWith = null; nod = 0f; headTarget = null; idleClip = "Idle"; NextActivity(); break; }
                    lookAt = chatWith.transform.position; headTarget = chatWith.transform.position + Vector3.up * 1.55f;
                    if (chatLead)   // the one who started keeps the turns in step
                    {
                        turnT -= dt;
                        if (turnT <= 0f)
                        {
                            speaking = !speaking; chatWith.speaking = !speaking; turnT = Random.Range(2.2f, 4.5f);
                            if (CamDist(transform.position) < 20f && Random.value < 0.7f) { if (speaking) Bark(SmallTalk(), null, 2.4f); else chatWith.Bark(Random.value < 0.5f ? Reply() : chatWith.SmallTalk(), null, 2.4f); }
                        }
                    }
                    idleClip = speaking ? "Talk" : "Idle"; nod = speaking ? 0f : 0.7f;
                    if (chatLead && stateT > chatLen && speaking) EndChat(true);
                    break;
                case "together":
                    if (!companion || companion.Dead || companion.follower != this || !companion.Calm) { companion = null; headTarget = null; NextActivity(); break; }
                    {
                        Vector3 lp = companion.transform.position; float d = Vector3.Distance(transform.position, lp);
                        if (companion.State == "chat" || companion.State == "browse" || !companion.Moving)
                        {
                            if (d > 1.8f) GoTo(lp + companion.transform.right * 0.95f, walkSpeed * 1.2f); else { Stop(); lookAt = lp; headTarget = lp + Vector3.up * 1.5f; idleClip = stateT % 6f < 3f ? "Talk" : "Idle"; }
                        }
                        else
                        {
                            Vector3 slot = lp + companion.transform.right * 0.9f + companion.transform.forward * (d > 2.5f ? 0f : 0.15f);
                            GoTo(slot, Mathf.Max(companion.agent.speed * (d > 2f ? 1.25f : 1.02f), 0.6f), 2f); idleClip = "Idle";
                            if (stateT % 11f < dt && Random.value < 0.5f && CamDist(transform.position) < 22f) { Bark(Random.value < 0.5f ? Reply() : SmallTalk(), null, 2.4f); headTarget = companion.transform.position + Vector3.up * 1.5f; glanceHold = 2f; }
                        }
                    }
                    break;
                case "errand":
                    if (!errandShop || !errandShop.HasLiveKeeper || stateT > 70f) { NextActivity(); break; }
                    if (Arrived(1f)) { if (GoTo(browseSpot, walkSpeed * 0.85f, 2f)) SetState("enter"); else NextActivity(); }
                    break;
                case "enter":
                    if (Arrived(0.6f) || stateT > 18f) { Stop(); lookAt = browseLook; headTarget = browseLook + Vector3.up * 1.1f; SetState("browse"); browseLen = Random.Range(7f, 15f); paid = false; }
                    break;
                case "browse":
                    if (!errandShop) { NextActivity(); break; }
                    if (!paid && stateT > browseLen * 0.55f)
                    {
                        paid = true; var bi = errandShop.info; lookAt = bi.counter; headTarget = bi.counter + Vector3.up * 1.4f; idleClip = "Talk";
                        anim.PlayUpper("Give"); if (CamDist(transform.position) < 15f) Bark(Barks.Pick("Just this, thanks.", "How much is this?", "Can I pay by card?", "One of those, please.", "Keep the change."), null, 2.2f, false, 14f);
                        var k = errandShop.keeper; if (k && !k.Dead) k.Serve(transform.position);
                    }
                    else if (paid && stateT > browseLen * 0.55f + 2.5f) idleClip = "Idle";
                    if (stateT > browseLen) { idleClip = "Idle"; headTarget = null; GoTo(shopOut + (shopOut - errandShop.info.door).normalized * 1.5f, walkSpeed); SetState("leave"); }
                    break;
                case "shelter":   // waiting out the weather in a doorway, watching the street
                    if (Arrived(0.8f) && !Moving) { lookAt = noisePoint; pose = Weather == Atmosphere.WeatherKind.Snow ? ArmPose.Crossed : (stateT % 20f < 8f && phoneHabit > 0.3f ? ArmPose.PhoneText : ArmPose.None); headDown = pose == ArmPose.PhoneText; }
                    if ((!Wet && stateT > 5f) || stateT > 70f) { pose = ArmPose.None; headDown = false; if (!Wet && Random.value < 0.4f && CamDist(transform.position) < 20f) Bark(Barks.Pick("Finally.", "Looks like it stopped.", "Right, let's go."), null, 2f); NextActivity(); }
                    break;
                case "leave":
                    if (Arrived(1f) || stateT > 18f) NextActivity();
                    break;
                case "curious":
                    if (Arrived() || stateT > 12f) { Stop(); lookAt = noisePoint; headTarget = noisePoint + Vector3.up * 1.2f; SetState("look"); }
                    break;
                case "look":
                    if (stateT > 4f + personality * 3f) { headTarget = null; NextActivity(); }
                    break;
                case "gather":   // walk up to a body, not too close
                    if (Arrived(0.8f) || stateT > 12f) { Stop(); lookAt = noisePoint; SetState("watch"); if (Random.value < 0.45f) { pose = ArmPose.Point; pointAt = noisePoint + Vector3.up * 0.3f; } Bark(Barks.Pick("Oh my God...", "Is he dead?", "Somebody call an ambulance!", "Don't touch anything!", "Who did this?"), Barks.Alarm, 2.6f); }
                    break;
                case "watch":   // brave ones stop and stare at trouble, some phone it in
                    lookAt = noisePoint; headTarget = noisePoint + Vector3.up * 0.4f;
                    if (pose == ArmPose.Point && stateT > 2.5f) pose = ArmPose.None;
                    if (stateT > 3f && stateT - dt <= 3f && witnessed != null && !witnessed.reported && personality > 0.6f && Random.value < 0.5f) { StartPhoneReport(); break; }
                    if (stateT > Random.Range(10f, 18f)) { pose = ArmPose.None; headTarget = null; if (witnessed != null) witnessed.watchers = Mathf.Max(0, witnessed.watchers - 1); SetState("cautious"); GoTo(RandomPedNode(transform.position, 40f), walkSpeed * 0.8f); }
                    break;
                case "handsup":
                    pose = ArmPose.HandsUp;
                    if (Player) { lookAt = Player.transform.position; headTarget = Player.transform.position + Vector3.up * 1.5f; }
                    if (stateT > 6f || !PlayerAimingAtMe(14f))
                    {
                        pose = ArmPose.None; idleClip = "Idle";
                        var p = EscapePoint(Player ? Player.transform.position : transform.position, 18f, true);
                        witnessed = AddIncident(transform.position, "threat", true);
                        if (p.HasValue && GoTo(p.Value, walkSpeed * 1.6f)) { SetState("tohide"); threat = Player ? Player.transform.position : transform.position; } else SetState("cautious");
                    }
                    break;
                case "avoid":   // walking briskly away from an armed man, eyes on him
                    if (Player) headTarget = Player.transform.position + Vector3.up * 1.5f;
                    if (Arrived(1f) || stateT > 10f) { headTarget = null; SetState("cautious"); GoTo(RandomPedNode(transform.position, 40f), walkSpeed); }
                    break;
                case "cower":   // frozen, hands over the head
                    pose = ArmPose.Cower; idleClip = "Crouch_Idle"; lookAt = threat;
                    if (stateT > 2.5f + personality * 8f) { pose = ArmPose.None; idleClip = "Idle"; StartFlee(threat); }
                    break;
                case "flee":
                    if (Arrived() || stateT > 9f)
                    {
                        var hp = EscapePoint(threat, 14f, true);
                        if (hp.HasValue && stateT < 9f && Vector3.Distance(hp.Value, threat) > 18f && GoTo(hp.Value, runSpeed)) SetState("tohide");
                        else EnterHide();
                    }
                    break;
                case "tohide":
                    if (Arrived() || stateT > 7f) EnterHide();
                    break;
                case "hide":
                    idleClip = "Crouch_Idle"; lookAt = threat; headTarget = threat + Vector3.up * 1.2f;
                    if (stateT > 5f) pose = ArmPose.None;
                    if (stateT > 4f && stateT - dt <= 4f && witnessed != null && witnessed.byPlayer && !witnessed.reported && !WantedSystem.PoliceOff && personality > 0.3f && Random.value < 0.55f) { StartPhoneReport(); break; }
                    if (stateT > 10f + personality * 8f || (stateT > 5f && Wanted && Wanted.Level == 0 && scared < 0.7f)) { idleClip = "Idle"; pose = ArmPose.None; SetState("cautious"); GoTo(RandomPedNode(transform.position, 40f), walkSpeed * 0.7f); }
                    break;
                case "phone":   // calling it in: phone to the ear, describing what they saw
                    pose = ArmPose.PhoneCall; idleClip = "Talk"; headTarget = threat + Vector3.up * 1.2f;
                    if (stateT > 2.5f && stateT - dt <= 2.5f) Bark(Barks.Pick("He went that way...", "Yes, I saw everything.", "Please hurry!", "Dark jacket, I think."), Barks.Alarm, 2.4f);
                    if (stateT > 6f)
                    {
                        if (witnessed != null && !witnessed.reported) { witnessed.reported = true; PoliceBrain.Report(witnessed.pos, witnessed.byPlayer); }
                        pose = ArmPose.None; idleClip = "Idle"; headTarget = null; SetState("cautious"); GoTo(RandomPedNode(transform.position, 40f), walkSpeed * 0.8f);
                    }
                    break;
                case "cautious":   // walking away, looking back now and then
                    if (stateT % 3f < dt && Random.value < 0.6f) { headTarget = threat + Vector3.up * 1.2f; glanceHold = 1.2f; }
                    if (Arrived() || stateT > 14f) { headTarget = null; NextActivity(); }
                    break;
                case "home":
                    if (Arrived(1.3f) || stateT > 70f)
                    {
                        // go inside: out of sight, out of the world until NightLife brings them back in the morning
                        if (!Night && NightLife.Fraction(Hour) > 0.6f) { NextActivity(); break; }
                        gameObject.SetActive(false); return;
                    }
                    break;
                case "medic_go":
                    if (!patient || (patient.Dead && stateT > 20f)) { NextActivity(); break; }
                    headTarget = patient.ChestPos;
                    if (Vector3.Distance(transform.position, patient.transform.position) < 1.8f) { Stop(); lookAt = patient.transform.position; SetState("medic_treat"); anim.PlayUpper("Give"); idleClip = "Crouch_Idle"; Bark(Barks.Pick("Stay with me!", "Let me see...", "Hold still.", "You'll be fine."), null, 2.2f); }
                    else if (stateT > 25f) NextActivity();
                    else if (Arrived()) GoTo(patient.transform.position, walkSpeed * 1.3f);
                    break;
                case "medic_treat":
                    if (stateT > 3f)
                    {
                        idleClip = "Idle";
                        if (patient && !patient.Dead) { patient.Treat(); GameManager.I?.Say($"{displayName} treated a wounded pedestrian"); }
                        else { if (patient) attended.Add(patient); Bark(Barks.Pick("He's gone.", "There's nothing I can do.", "Too late..."), null, 2.4f); }
                        patient = null; NextActivity();
                    }
                    break;
            }
            scared = Mathf.Max(0f, scared - 0.2f * dt);
            // never loiter on the carriageway: if a calm NPC is standing still on the road, walk to the nearest pavement
            if ((State == "wait" || State == "chat" || State == "watch" || State == "look" || State == "handsup" || State == "cautious" || State == "wander") && !Moving && OnRoad() && OffRoadPoint(transform.position, 14f, out var pave))
            {
                // aim a step past the kerb, so they don't stop with their heels still on the road; if that keeps failing, just put them on the pavement
                Stat("civ.offroad." + State); if (chatWith) EndChatQuiet(); idleClip = "Idle"; pose = ArmPose.None;
                Vector3 dir = pave - transform.position; dir.y = 0f; if (dir.sqrMagnitude > 0.01f && OffRoadPoint(pave + dir.normalized * 1.3f, 1.5f, out var deeper)) pave = deeper;
                offroadTries++;
                if (offroadTries > 2) { offroadTries = 0; agent.stoppingDistance = 0.4f; agent.Warp(pave); NextActivity(); }
                else if (GoTo(pave, walkSpeed * 1.3f)) { SetState("cautious"); agent.stoppingDistance = 0.1f; }
            }
            else if (offroadTries > 0 && !OnRoad()) { offroadTries = 0; agent.stoppingDistance = 0.4f; }
            React(dt);

            // medics look for work
            if (isMedic && (State == "wander" || State == "wait") && Random.value < dt)
            {
                CharacterBody best = null; float bd = 40f * 40f;
                foreach (var b in CharacterBody.All)
                {
                    if (!b.Dead) attended.Remove(b);
                    if (b == body || b.isPlayer || (b.Dead && attended.Contains(b)) || (!b.Dead && b.InjuryLeg == 0 && b.InjuryArm == 0)) continue;
                    float d = (b.transform.position - transform.position).sqrMagnitude; if (d < bd) { bd = d; best = b; }
                }
                if (best && (!best.Dead || best.KilledBy != null)) { ClearErrand(); EndCompany(false); patient = best; SetState("medic_go"); GoTo(best.transform.position, walkSpeed * 1.3f); }
            }
        }

        /// <summary>Arrived somewhere: stand for a bit and do one of the things people do while standing.</summary>
        void StartIdle()
        {
            Stop(); SetState("wait"); stateT = -Random.Range(3f, 10f) * (Wet ? 0.4f : 1f); activity = ""; pose = ArmPose.None; headDown = false; idleClip = "Idle";
            if (Weather == Atmosphere.WeatherKind.Snow) { pose = ArmPose.Crossed; headDown = true; return; }   // hugging themselves in the cold
            if (follower) { stateT = -Random.Range(3f, 6f); lookAt = follower.transform.position; headTarget = follower.transform.position + Vector3.up * 1.5f; idleClip = "Talk"; return; }
            float r = Random.value;
            if (r < phoneHabit) { activity = "text"; pose = ArmPose.PhoneText; headDown = true; stateT = -Random.Range(4f, 9f); }
            else if (r < phoneHabit + 0.08f) { activity = "call"; pose = ArmPose.PhoneCall; idleClip = "Talk"; talkT = 0.5f; stateT = -Random.Range(6f, 11f); }
            else if (r < phoneHabit + 0.2f) { pose = personality > 0.6f ? ArmPose.Hips : ArmPose.Crossed; }
            else if (r < phoneHabit + 0.32f && Night) { idleClip = personality > 0.75f ? "Dance" : "Idle"; }   // a little tipsy on the way home
        }

        /// <summary>Phone the police about what they saw.</summary>
        void StartPhoneReport()
        {
            if (WantedSystem.PoliceOff && (witnessed == null || witnessed.kind != "body")) return;
            Stop(); SetState("phone"); pose = ArmPose.PhoneCall; idleClip = "Talk";
            Bark(witnessed != null && witnessed.kind == "body" ? Barks.Pick("Hello? There's a body here!", "I need an ambulance, and the police!") : Barks.Pick("Police? Someone's shooting!", "Hello, police? There's a man with a gun!", "Please send someone, quick!"), Barks.Alarm, 2.6f);
        }

        protected override void OnStuck(int times) { if (times <= 1) base.OnStuck(times); else if (Calm && State != "together") NextActivity(); else base.OnStuck(times); }
    }
}
