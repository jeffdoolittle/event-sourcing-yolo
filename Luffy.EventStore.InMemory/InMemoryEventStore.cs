﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Luffy.EventStore.Exceptions;

namespace Luffy.EventStore.InMemory
{
  public class InMemoryEventStore : IEventStore
  {
    private readonly Stream _allEvents = new();
    private readonly EventsPerStreamDictionary _eventsPerStream = new();

    public bool IsEmpty()
    {
      return _allEvents.IsEmpty();
    }

    public IAppendToStreamResponse AppendToStream(UInt64 expectedStreamRevision, string streamId,
      IEnumerable<IEventData> events)
    {
      return DoAppendToStream(streamId, events, StreamState.Any, expectedStreamRevision);
    }

    public IAppendToStreamResponse AppendToStream(StreamState expectedStreamState, string streamId,
      IEnumerable<IEventData> events)
    {
      return DoAppendToStream(streamId, events, expectedStreamState, null);
    }

    public IEnumerable<IRecordedEvent> ReadStream(ReadDirection readDirection, string streamId,
      UInt64 fromStreamRevision,
      UInt64 howMany)
    {
      // TODO: Use readDirection, fromStreamRevision, and howMany config
      return _eventsPerStream[streamId];
    }

    public IEnumerable<IRecordedEvent> ReadAll(ReadDirection direction, UInt64 howMany)
    {
      return _allEvents;
    }

    private AppendToStreamResponse DoAppendToStream(
      string streamId,
      IEnumerable<IEventData> events,
      StreamState expectedStreamState,
      UInt64? expectedStreamRevision)
    {
      EnsureStreamState(streamId, expectedStreamState);
      EnsureStreamRevision(streamId, expectedStreamState, expectedStreamRevision);

      foreach (var @event in events)
      {
        AppendRecordedEvent(streamId, new RecordedEvent
        {
          EventId = Guid.NewGuid(),
          Created = DateTime.Now,
          Data = @event.Data,
          Metadata = @event.Metadata,
          Type = @event.EventType,
          EventStreamId = streamId,
          GlobalEventRevision = NextGlobalEventRevision(),
          StreamEventRevision = NextStreamEventRevision(streamId),
        });
      }

      return new AppendToStreamResponse
      {
        NextExpectedStreamRevision = StreamRevision.ToStreamRevision(GetStream(streamId).Count())
      };
    }

    private void EnsureStreamRevision(string streamId, StreamState expectedStreamState, UInt64? expectedStreamRevision)
    {
      if (StreamState.Any == expectedStreamState && expectedStreamRevision == null)
      {
        return;
      }

      var expected = expectedStreamRevision ?? NextStreamEventRevision(streamId);
      var actual = NextStreamEventRevision(streamId);

      if (expected != actual)
      {
        throw new ExpectedStreamRevisionException(streamId, expected, actual);
      }
    }

    private Stream GetStream(string streamId)
    {
      return _eventsPerStream.GetStream(streamId);
    }

    private UInt64 NextGlobalEventRevision()
    {
      return StreamRevision.ToStreamRevision(_allEvents.Count());
    }

    private UInt64 NextStreamEventRevision(string streamId)
    {
      return StreamRevision.ToStreamRevision(GetStream(streamId).Count());
    }

    private void AppendRecordedEvent(string streamId, RecordedEvent recordedEvent)
    {
      _eventsPerStream[streamId].Add(recordedEvent);
      _allEvents.Add(recordedEvent);
    }

    private void EnsureStreamState(string streamId, StreamState expectedStreamState)
    {
      if (_eventsPerStream.ContainsKey(streamId))
      {
        if (StreamState.NoStream == expectedStreamState)
        {
          throw new ExpectedNoStreamException(streamId);
        }

        return;
      }

      if (StreamState.StreamExists == expectedStreamState)
      {
        throw new ExpectedStreamExistsException(streamId);
      }

      _eventsPerStream.TryAdd(streamId, new Stream());
    }
  }
}
