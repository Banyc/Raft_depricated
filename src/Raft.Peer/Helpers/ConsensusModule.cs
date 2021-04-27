using System.Xml.Linq;
using System.Linq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Raft.Peer.Models;

namespace Raft.Peer.Helpers
{
    public partial class ConsensusModule
    {
        private readonly ConsensusState state = new();
        // The election timeout is the amount of time a follower waits until becoming a candidate.
        private readonly System.Timers.Timer timerElectionTimeout = new();
        private readonly System.Timers.Timer timerHeartbeatTimeout = new();
        private readonly ConsensusSettings settings;
        private readonly ConsensusStateMachine stateMachine;
        private readonly Random random = new();

        // public delegate AppendEntriesReply AppendEntriesEventHandler(ConsensusModule sender, int targetPeerId, AppendEntriesArgs arguments);
        // public event AppendEntriesEventHandler SendAppendEntries;
        public delegate Task<AppendEntriesReply> AppendEntriesAsyncEventHandler(ConsensusModule sender, int targetPeerId, AppendEntriesArgs arguments, CancellationToken cancellationToken);
        public event AppendEntriesAsyncEventHandler SendAppendEntriesAsync;

        // public delegate RequestVoteReply RequestVoteEventHandler(ConsensusModule sender, int targetPeerId, RequestVoteArgs arguments);
        // public event RequestVoteEventHandler SendRequestVote;
        public delegate Task<RequestVoteReply> RequestVoteAsyncEventHandler(ConsensusModule sender, int targetPeerId, RequestVoteArgs arguments, CancellationToken cancellationToken);
        public event RequestVoteAsyncEventHandler SendRequestVoteAsync;

        public ConsensusModule(ConsensusSettings settings, ConsensusStateMachine stateMachine)
        {
            this.settings = settings;
            this.stateMachine = stateMachine;

            // timer {
            this.timerElectionTimeout.AutoReset = false;
            this.timerHeartbeatTimeout.AutoReset = false;

            this.timerHeartbeatTimeout.Interval =
                this.settings.TimerHeartbeatTimeout.TotalMilliseconds;

            this.timerElectionTimeout.Elapsed += TimerElectionTimeout_Elapsed;
            this.timerHeartbeatTimeout.Elapsed += TimerHeartbeatTimeout_Elapsed;

            // }

        }

        public void Start()
        {
            ConditionalInitiateTimerElectionTimeout();
        }

        private void InitiateLeaderState()
        {
            this.state.NextIndex.Clear();
            this.state.MatchIndex.Clear();
            int lastLogIndex = this.state.PersistentState.Log.Count - 1;
            int i;
            for (i = 0; i < this.settings.PeerCount; i++)
            {
                this.state.NextIndex.Add(lastLogIndex + 1);
                this.state.MatchIndex.Add(0);
            }
        }

        private void InitiateTimerElectionTimeoutInterval()
        {
            this.timerElectionTimeout.Interval = NextDouble(
                this.settings.TimerElectionTimeoutLowerBound.TotalMilliseconds,
                this.settings.TimerElectionTimeoutHigherBound.TotalMilliseconds
            );
        }

        private void ConditionalInitiateTimerElectionTimeout()
        {
            if (this.state.ServerState == ServerState.Follower ||
                this.state.ServerState == ServerState.Candidate)
            {
                InitiateTimerElectionTimeoutInterval();
                this.timerElectionTimeout.Start();
            }
        }

        private double NextDouble(double minValue, double maxValue)
        {
            double x = this.random.NextDouble();
            double a = minValue;
            double b = maxValue;

            return
                (b - a) * x + a;
        }

        private void StepDown(int newTerm)
        {
            this.state.ServerState = ServerState.Follower;
            this.state.PersistentState.CurrentTerm = newTerm;
            this.timerHeartbeatTimeout.Stop();
        }

        // candidate -{requestVote}-> followers
        // this := candidate
        private void TimerElectionTimeout_Elapsed(object sender, ElapsedEventArgs e)
        {
            // After the election timeout the follower becomes a candidate
            this.state.ServerState = ServerState.Candidate;
            // and starts a new election term...
            this.state.PersistentState.CurrentTerm++;
            // ...votes for itself...
            this.state.PersistentState.VotedFor = this.settings.ThisPeerId;
            this.state.PersistentState.VoteCount = 0;
            this.state.PersistentState.VoteCount++;

            // send requestVotes
            DoRequestVote();

            // reset election timer
            ConditionalInitiateTimerElectionTimeout();
        }

        // leader -{heartbeat}-> followers
        // this := leader
        private void TimerHeartbeatTimeout_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (this.state.ServerState == ServerState.Follower ||
                this.state.ServerState == ServerState.Candidate)
            {
                this.timerHeartbeatTimeout.Stop();
                return;
            }
            if (this.state.ServerState == ServerState.Leader)
            {
                DoAppendEntries();
            }
            this.timerHeartbeatTimeout.Start();
        }
    }
}