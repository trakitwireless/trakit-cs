# Trak-iT API Synchronization

This library provides a full suite of tools to keep a local copy of objects from Trak-iT's APIs in-sync.
Other Trak-iT API libraries are available on [GitHub](https://github.com/trakitwireless) and [nuget](https://www.nuget.org/profiles/Trak-iT).

### Prerequisites

The `Trakit.Commands` package is required as since this library sends requests to the APIs.  It also contains the `Trakit.Tools` namespace to deserialize objects in the responses.
The `Trakit.Objects` package is required as most `Response` classes will contain an object from that library.
We rely on the `Newtonsoft.Json` package for serialization between your application and the Trak-iT API services.

## Questions and Feedback

If you have any questions, please start for the project on GitHub
https://github.com/trakitwireless/trakit-cs-sync/issues
