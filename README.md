# Gatekeeper

> An access-control SaaS built in public, one quality gate at a time.

Gatekeeper is the running example and companion code for the book
**[AI: Programming Like a God: The Heavenly Gates](https://leanpub.com/ai-programming-like-a-god)**
by Tom Gilkison.

The book's premise: AI can write the code, but it cannot be trusted to
ship it. Gatekeeper starts life as an AI-generated app with no tests,
no CI, and no guardrails. Across the book it earns a layered system of
quality "gates" until its code is safe to merge on green, with the
human designing and verifying instead of typing.

## Follow along

Each chapter adds one gate. Every chapter's end state is a git tag, so
you can check out the code exactly as it stands at any point in the
book:

```bash
git clone https://github.com/TGilkison/Gatekeeper.git
cd Gatekeeper
git checkout chapter-06   # the code at the end of Chapter 6
```

## Tech

C# / .NET 8, ASP.NET Core Blazor Web App. Chosen for the strongest,
most cohesive quality-gate tooling of any mainstream stack: a real
compiler as the first gate, Roslyn analyzers, NetArchTest for
architecture, and Stryker.NET for mutation testing. The book explains
the why.

## License

MIT. Use it, fork it, copy the gate configs into your own projects.
That is the point: the book teaches you to build these gates; this repo
is where you can see them working and lift them.

## The book

[AI: Programming Like a God: The Heavenly Gates](https://leanpub.com/ai-programming-like-a-god)
