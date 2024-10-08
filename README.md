# Trak-iT API Synchronization

This library provides a full suite of tools to keep a local copy of objects from Trak-iT's APIs in-sync.
Other Trak-iT API libraries are available on GitHub.
https://github.com/trakitwireless

### Prerequisites

The `trakit.commands` package is required as since this library sends requests to the APIs.
The `trakit.tools` package is required since the commands need to deserialize objects in the responses.
The `trakit.objects` package is required as the JSON converters works for those objects.
We rely on the Newtonsoft.Json package for serialization between your application and the server.

## Questions and Feedback

If you have any questions, please start for the project on GitHub
https://github.com/trakitwireless/trakit-dotnet/issues
