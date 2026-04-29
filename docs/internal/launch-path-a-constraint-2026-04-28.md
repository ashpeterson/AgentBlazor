# Path A Constraint

Saved from the 2026-04-28 launch review thread.

June 9 is 6 weeks from today. That's tight but possible. Before the schedule, name the failure mode for Path A specifically, because Path A has one and it's not the same as the build-in-private one.
The Path A failure mode
The risk on this path isn't "won't ship." It's "scope creeps back in under the name of polish." Cutting things is harder than building them because every cut feels like loss. Within two weeks you'll find yourself thinking "actually the CLI does help adoption, let me just make the docs cleaner instead of hiding it" or "the SQLite tier is already built, surely I should at least mention it." Each individual concession is defensible. The aggregate is no longer Path A.
The discipline this path requires is: every week is for cutting and polishing the cut version, not for adding back what you cut. If you find yourself touching a file in AgentBlazor.Licensing in the next 6 weeks for any reason other than removing it from the public surface, that's the signal you've drifted.
The 6-week schedule
Today is Tuesday April 28. Each week ends Sunday. June 9 is the launch Tuesday.
Week 1 (Apr 28 – May 3): Cut. No new code. Only deletion or hiding.
Move runtime-realignment-plan.md, REFACTOR_STATUS.md, PRODUCTION_PLAN.md, plan.md, the various nuget-prerelease checklists, and the master plan documents to docs/internal/ or delete them. They are working documents, not public-facing. Their existence in the repo root signals "still cooking" to anyone browsing the project.
Remove AgentBlazor.Licensing from AgentBlazor.slnx (it's already removed from .slnx but still in .sln — pick one solution file and delete the other; REFACTOR_STATUS.md says this should have happened already). Delete the project directory. The Pro tier is gone for v1.
In src/AgentBlazor.Components/AgentBlazor.Components.csproj, change the package description from "Free tier includes full runtime. Pro tier adds analytics, audit logging, and smart suggestions" to a single sentence about what the package does. Pro tier doesn't exist for v1.
Strip every reference to "Free / Paid / Premium" from the README, the demo landing page, the docs, and any public-facing surface. Search the repo: grep -r "Premium\|Paid tier\|Pro tier" --include="*.md" --include="*.razor" --include="*.cs". All of it goes.
Move src/AgentBlazor.Cli/README.md content into docs/advanced/cli.md. The CLI is not the headline, it's an advanced setup option referenced once at the bottom of the main README.
Fix gpt-5.4-mini in the README and anywhere else it appears. Use a model string that exists today.
End-of-week gate: a stranger browsing the repo root sees one README, one demo, one starter sample, and no marketing references to a paid tier. If they don't, week 1 isn't done.
Week 2 (May 4 – May 10): NuGet.org and new README.
Verify the package name AgentBlazor is available on nuget.org. Reserve it. Publish 0.1.0-preview.10 (or just 0.1.0 — drop the preview suffix; "preview" reads as "don't use this yet"). The same dotnet add package AgentBlazor line that's currently a lie in the README must work.
Rewrite the README from scratch. Not "trim in half" — rewrite. The current version reads like internal product documentation. The new version is one screen tall and answers three questions a stranger has in this order: what is this, how do I install it, what does it look like working. Pitch in one sentence. A 10-second screen recording or GIF of the demo doing one thing. A 5-line install. One [AgentAction] example. Link to demo and starter. Stop.
The "What It Is / What It Is Not" section in the current README is defensive ("not a chat-for-clicking gimmick") — that's arguing with a critic who doesn't exist. Cut it.
End-of-week gate: dotnet add package AgentBlazor from a fresh project, in a fresh terminal, on a different machine, succeeds. Test this on your machine and a friend's. If you can't run it on a different machine, week 2 isn't done.
Week 3 (May 11 – May 17): Demo replacement.
This is the highest-risk week because it's the only one with real build work. The current demo workflows ("response orchestration", "release dossier") are unusable as a marketing surface. Replace them with one demo using a problem any .NET dev recognises immediately.
Suggestions, pick one: a contact/CRM page (search contacts, add a note, schedule a follow-up), a help-desk page (filter tickets by status, draft a reply, escalate), a basic content-management page (find drafts, schedule, publish). Each of these maps directly onto the existing capability/workflow infrastructure. None of them require new architecture.
Three demo prompts a normal person can read: "Find tickets from this week that haven't been answered." "Draft a reply for ticket #4." "Mark this ticket resolved and notify the customer." That kind of concreteness.
Hard rule for this week: do not build new components. Use what's already there. The MudBlazor wrappers are sufficient for any of those use cases.
End-of-week gate: a developer who has never seen AgentBlazor can open /demo, type one of the three prompts, and have something visibly happen that they understand. Test this on the same friend from week 2.
Week 4 (May 18 – May 24): Landing page and content.
One-page landing. Carrd, Vercel, Astro — pick whatever takes the least time. The page has: pitch (one sentence), GIF/video (the same one from the README), install command, link to GitHub, link to demo, email signup. No pricing, no features grid, no testimonials section that's empty.
Draft three pieces of content: a "why AgentBlazor exists" post (Dev.to or your own blog), a Reddit post for r/dotnet and r/Blazor, an X/LinkedIn thread. All three drafted, not published, by end of week.
End-of-week gate: drafts exist, landing page is live but unannounced, the GIF in the README also lives on the landing page.
Week 5 (May 25 – May 31): Beta and freeze.
Find 3–5 .NET developers, via Reddit DM or Discord, willing to install the package and report back. Frame it as "I'm shipping in two weeks, would 30 minutes of your time before launch help me catch obvious problems?" Most will say no. Three saying yes is enough.
Fix what they hit. Code freeze on Saturday May 30. Anything broken after that ships broken or doesn't ship.
End-of-week gate: 3 outside developers have installed the package and at least one of them ran the demo successfully without you on a call.
Week 6 (Jun 1 – Jun 7): Final polish.
Final README pass. Schedule the posts. Tag v0.1.0. Make sure NuGet.org has the latest version. The launch Tuesday is June 9 — you want everything in place by Sunday June 7 so Monday is a buffer day for whatever broke over the weekend.
Launch (Tuesday June 9): Reddit, LinkedIn, X, blog post, Show HN — same day. Then engage with everything that comes back for the rest of that week.
What this schedule does not include
No new components. No new tests. No new architecture refactor. No protocol work (AG-UI compliance, MCP, whatever). No additional providers. No paid tier work. No AgentBlazor.Insights or AgentBlazor.RuntimeAdapter reorganisation that the realignment plan describes. No more renaming. No more positioning passes after week 2.
Every one of those things might be a good idea eventually. None of them belong in this 6 weeks. If you find yourself reaching for any of them, you've started Path C: keep refining until it's right.
The forcing function
The 6-week plan only works if there's a public commitment that makes drifting visible. Path A privately means nothing — you've privately committed to several things in this conversation already.
By Friday May 1 (this Friday), post in r/dotnet: "Shipping AgentBlazor v1 (Blazor-native AI agent UX layer) on June 9. Looking for 3 beta testers in the last two weeks of May. Repo here, what it does in one paragraph here." That post does three things: it forces clarity (you can't write the paragraph if the pitch is still mushy), it dates the commitment publicly, and it starts the audience-building work that the previous review identified as the actual gap.
The post going up by Friday is the test. If it doesn't go up, the commitment didn't hold and the conversation we'll need to have is different from the one we're having now.
So: are you posting it by Friday, or not?
