﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Paps.FSM
{
    public class FSM<TState, TTrigger> : IFSMWithGuardConditions<TState, TTrigger>
    {
        public int StateCount => _states.Count;
        public int TransitionCount => _transitions.Count;
        public bool IsStarted { get; private set; }
        public TState InitialState { get; private set; }

        private IFSMState<TState, TTrigger> _currentState;

        private Dictionary<TState, IFSMState<TState, TTrigger>> _states;
        private HashSet<FSMTransition<TState, TTrigger>> _transitions;
        private Dictionary<FSMTransition<TState, TTrigger>, Func<TState, TTrigger, TState, bool>> _ANDguardConditions;
        private Dictionary<FSMTransition<TState, TTrigger>, Func<TState, TTrigger, TState, bool>> _ORguardConditions;

        private Queue<TransitionRequest> _transitionRequestQueue;

        private bool _isTransitioning;

        private Func<TState, TState, bool> _stateComparer;
        private Func<TTrigger, TTrigger, bool> _triggerComparer;

        private FSMStateEqualityComparer _stateEqualityComparer;

        public event StateChanged<TState, TTrigger> OnStateChanged;

        public FSM(Func<TState, TState, bool> stateComparer, Func<TTrigger, TTrigger, bool> triggerComparer)
        {
            _stateComparer = stateComparer;
            _triggerComparer = triggerComparer;

            _stateEqualityComparer = new FSMStateEqualityComparer(stateComparer);
            var transitionEqualityComparer = new FSMTransitionEqualityComparer();

            _states = new Dictionary<TState, IFSMState<TState, TTrigger>>(_stateEqualityComparer);
            _transitions = new HashSet<FSMTransition<TState, TTrigger>>();
            _ANDguardConditions = new Dictionary<FSMTransition<TState, TTrigger>, Func<TState, TTrigger, TState, bool>>(transitionEqualityComparer);
            _ORguardConditions = new Dictionary<FSMTransition<TState, TTrigger>, Func<TState, TTrigger, TState, bool>>(transitionEqualityComparer);
            _transitionRequestQueue = new Queue<TransitionRequest>();
        }

        public FSM() : this(DefaultComparer, DefaultComparer)
        {
            
        }

        public void SetStateComparer(Func<TState, TState, bool> comparer)
        {
            _stateComparer = comparer;
            _stateEqualityComparer._stateComparer = comparer;
        }

        public void SetTriggerComparer(Func<TTrigger, TTrigger, bool> comparer)
        {
            _triggerComparer = comparer;
        }

        private static bool DefaultComparer<T>(T first, T second)
        {
            return first.Equals(second);
        }

        public void Start()
        {
            ValidateCanStart();

            _currentState = GetStateById(InitialState);
            EnterCurrentState();

            IsStarted = true;
        }

        private void ValidateCanStart()
        {
            ValidateIsNotStarted();
            ValidateInitialState(InitialState);
        }
        
        private void EnterCurrentState()
        {
            _currentState.Enter();
        }

        public void Update()
        {
            UpdateCurrentState();
        }

        private void UpdateCurrentState()
        {
            ValidateIsStarted();

            _currentState.Update();
        }

        public void Stop()
        {
            if(IsStarted)
            {
                ExitCurrentState();
            }

            IsStarted = false;
        }

        private void ExitCurrentState()
        {
            _currentState.Exit();
        }

        public IFSM<TState, TTrigger> SetInitialState(TState stateId)
        {
            ValidateCanSetInitialState(stateId);

            InternalSetInitialState(stateId);

            return this;
        }

        private void ValidateCanSetInitialState(TState initialStateId)
        {
            ValidateIsNotStarted();
            ValidateInitialState(initialStateId);
        }

        private void ValidateInitialState(TState initialStateId)
        {
            if(ContainsState(initialStateId) == false)
            {
                throw new InvalidInitialStateException();
            }
        }

        private void ValidateIsStarted()
        {
            if (IsStarted == false)
            {
                throw new FSMNotStartedException();
            }
        }

        private void ValidateIsNotStarted()
        {
            if (IsStarted)
            {
                throw new FSMStartedException();
            }
        }

        private void ThrowExceptionIfInitialStateIsInvalid()
        {
            ValidateHasStateWithId(InitialState);
        }

        private void InternalSetInitialState(TState stateId)
        {
            InitialState = stateId;
        }

        public IFSM<TState, TTrigger> AddState(TState stateId, IFSMState<TState, TTrigger> state)
        {
            ValidateCanAddState(stateId, state);

            InternalAddState(stateId, state);

            return this;
        }

        private void ValidateCanAddState(TState stateId, IFSMState<TState, TTrigger> state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }
            else if (_states.ContainsKey(stateId))
            {
                throw new StateIdAlreadyAddedException("State id " + stateId + " was already added to state machine");
            }
        }

        private void InternalAddState(TState stateId, IFSMState<TState, TTrigger> state)
        {
            _states.Add(stateId, state);
        }

        public void ForeachState(ReturnTrueToFinishIteration<IFSMState<TState, TTrigger>> finishable)
        {
            foreach(IFSMState<TState, TTrigger> state in _states.Values)
            {
                if(finishable(state))
                {
                    break;
                }
            }
        }

        public IFSM<TState, TTrigger> RemoveState(TState stateId)
        {
            if(_states.Remove(stateId))
            {
                RemoveTransitionsRelatedTo(stateId);
            }

            return this;
        }

        private void RemoveTransitionsRelatedTo(TState stateId)
        {
            FSMTransition<TState, TTrigger>[] transitions = _transitions.Where(
                (transition) => _stateComparer(transition.StateFrom, stateId) || _stateComparer(transition.StateTo, stateId)
                ).ToArray();

            for(int i = 0; i < transitions.Length; i++)
            {
                var transition = transitions[i];

                InternalRemoveTransition(transition.StateFrom, transition.Trigger, transition.StateTo);
            }
        }

        private IFSMState<TState, TTrigger> GetStateById(TState stateId)
        {
            foreach(IFSMState<TState, TTrigger> state in _states.Values)
            {
                if(_stateComparer(GetIdOf(state), stateId))
                {
                    return state;
                }
            }

            throw new StateNotAddedException(stateId.ToString());
        }

        private IFSMState<TState, TTrigger> TryGetStateById(TState stateId)
        {
            try
            {
                var state = GetStateById(stateId);
                return state;
            }
            catch(StateNotAddedException e)
            {
                return null;
            }
        }

        public IFSM<TState, TTrigger> AddTransition(TState stateFrom, TTrigger trigger, TState stateTo)
        {
            ValidateHasStateWithId(stateFrom);
            ValidateHasStateWithId(stateTo);

            InternalAddTransition(stateFrom, trigger, stateTo);

            return this;
        }

        private void InternalAddTransition(TState stateFrom, TTrigger trigger, TState stateTo)
        {
            _transitions.Add(new FSMTransition<TState, TTrigger>(stateFrom, trigger, stateTo));
        }

        public IFSM<TState, TTrigger> RemoveTransition(TState stateFrom, TTrigger trigger, TState stateTo)
        {
            ValidateHasStateWithId(stateFrom);
            ValidateHasStateWithId(stateTo);

            InternalRemoveTransition(stateFrom, trigger, stateTo);

            return this;
        }

        private void InternalRemoveTransition(TState stateFrom, TTrigger trigger, TState stateTo)
        {
            var comparisonTransition = new FSMTransition<TState, TTrigger>(stateFrom, trigger, stateTo);

            if (_transitions.Remove(comparisonTransition))
            {
                RemoveAllGuardConditionsRelatedTo(comparisonTransition);
            }
        }

        private void RemoveAllGuardConditionsRelatedTo(FSMTransition<TState, TTrigger> transition)
        {
            _ANDguardConditions.Remove(transition);
            _ORguardConditions.Remove(transition);
        }

        private void ValidateHasStateWithId(TState stateId)
        {
            if(ContainsState(stateId) == false)
            {
                throw new StateNotAddedException(stateId.ToString());
            }
        }

        public void ForeachTransition(ReturnTrueToFinishIteration<IFSMTransition<TState, TTrigger>> finishable)
        {
            foreach(FSMTransition<TState, TTrigger> transition in _transitions)
            {
                if(finishable(transition))
                {
                    break;
                }
            }
        }

        public bool IsInState(TState stateId)
        {
            if(_currentState != null)
            {
                return _stateComparer(GetIdOf(_currentState), stateId);
            }

            return false;
        }

        public bool ContainsState(TState stateId)
        {
            foreach(IFSMState<TState, TTrigger> state in _states.Values)
            {
                if(_stateComparer(GetIdOf(state), stateId))
                {
                    return true;
                }
            }

            return false;
        }

        public bool ContainsTransition(TState stateFrom, TTrigger trigger, TState stateTo)
        {
            FSMTransition<TState, TTrigger> comparisonTransition = new FSMTransition<TState, TTrigger>(stateFrom, trigger, stateTo);

            foreach(FSMTransition<TState, TTrigger> transition in _transitions)
            {
                if(transition == comparisonTransition)
                {
                    return true;
                }
            }

            return false;
        }

        public void Trigger(TTrigger trigger)
        {
            ValidateIsStarted();

            TState currentStateId = GetIdOf(_currentState);

            if (HasTransitionFromState(currentStateId, trigger))
            {
                _transitionRequestQueue.Enqueue(new TransitionRequest() { stateFrom = GetIdOf(_currentState), trigger = trigger });

                if(_isTransitioning == false)
                {
                    _isTransitioning = true;
                    TriggerQueued();
                }
            }
        }
        
        private void TriggerQueued()
        {
            while(_transitionRequestQueue.Count > 0)
            {
                TransitionRequest transition = _transitionRequestQueue.Dequeue();

                var stateTo = GetStateTo(transition.trigger);

                if (stateTo != null)
                {
                    Transition(_currentState, transition.trigger, stateTo);
                }
            }

            _isTransitioning = false;
        }

        private bool HasTransitionFromState(TState stateFrom, TTrigger trigger)
        {
            foreach (FSMTransition<TState, TTrigger> transition in _transitions)
            {
                if (_stateComparer(transition.StateFrom, stateFrom) && _triggerComparer(transition.Trigger, trigger))
                {
                    return true;
                }
            }

            return false;
        }

        private IFSMState<TState, TTrigger> GetStateTo(TTrigger trigger)
        {
            foreach(FSMTransition<TState, TTrigger> transition in _transitions)
            {
                if(_stateComparer(transition.StateFrom, GetIdOf(_currentState)) 
                    && _triggerComparer(transition.Trigger, trigger)
                    && IsValidTransition(transition))
                {
                    return TryGetStateById(transition.StateTo);
                }
            }

            return null;
        }

        private void Transition(IFSMState<TState, TTrigger> stateFrom, TTrigger trigger, IFSMState<TState, TTrigger> stateTo)
        {
            ExitCurrentState();
            
            _currentState = stateTo;

            CallOnStateChangedEvent(GetIdOf(stateFrom), trigger, GetIdOf(stateTo));

            EnterCurrentState();
        }

        private bool IsValidTransition(FSMTransition<TState, TTrigger> transition)
        {
            return AllANDGuardConditionsAreTrueOrDoesNotHave(transition) && AnyORGuardConditionIsTrueOrDoesNotHave(transition);
        }

        

        private bool AllANDGuardConditionsAreTrueOrDoesNotHave(FSMTransition<TState, TTrigger> transition)
        {
            return _ANDguardConditions.ContainsKey(transition) == false
                || ANDGuardConditionsAllTrue(transition);
        }

        private bool AnyORGuardConditionIsTrueOrDoesNotHave(FSMTransition<TState, TTrigger> transition)
        {
            return _ORguardConditions.ContainsKey(transition) == false
                || ORGuardConditionsAnyTrue(transition);
        }
        
        private bool ORGuardConditionsAnyTrue(FSMTransition<TState, TTrigger> transition)
        {
            Func<TState, TTrigger, TState, bool>[] guardConditions =
                Array.ConvertAll(_ORguardConditions[transition].GetInvocationList(),
                func => (Func<TState, TTrigger, TState, bool>)func);

            foreach (var guardCondition in guardConditions)
            {
                if (guardCondition(transition.StateFrom, transition.Trigger, transition.StateTo) == true)
                {
                    return true;
                }
            }

            return false;
        }

        private bool ANDGuardConditionsAllTrue(FSMTransition<TState, TTrigger> transition)
        {
            Func<TState, TTrigger, TState, bool>[] guardConditions =
                Array.ConvertAll(_ANDguardConditions[transition].GetInvocationList(), 
                func => (Func<TState, TTrigger, TState, bool>)func);

            foreach (var guardCondition in guardConditions)
            {
                if (guardCondition(transition.StateFrom, transition.Trigger, transition.StateTo) == false)
                {
                    return false;
                }
            }

            return true;
        }

        

        private void CallOnStateChangedEvent(TState stateFrom, TTrigger trigger, TState stateTo)
        {
            if(OnStateChanged != null)
            {
                OnStateChanged(stateFrom, trigger, stateTo);
            }
        }

        public TState GetIdOf(IFSMState<TState, TTrigger> state)
        {
            foreach(KeyValuePair<TState, IFSMState<TState, TTrigger>> entry in _states)
            {
                if(entry.Value == state)
                {
                    return entry.Key;
                }
            }

            throw new StateNotAddedException();
        }

        public IFSMWithGuardConditions<TState, TTrigger> AddANDGuardConditionTo(TState stateFrom, TTrigger trigger, TState stateTo, Func<TState, TTrigger, TState, bool> guardCondition)
        {
            AddGuardConditionTo(_ANDguardConditions, stateFrom, trigger, stateTo, guardCondition);

            return this;
        }

        public IFSMWithGuardConditions<TState, TTrigger> RemoveANDGuardConditionFrom(TState stateFrom, TTrigger trigger, TState stateTo, Func<TState, TTrigger, TState, bool> guardCondition)
        {
            RemoveGuardConditionFrom(_ANDguardConditions, stateFrom, trigger, stateTo, guardCondition);

            return this;
        }

        public IFSMWithGuardConditions<TState, TTrigger> AddORGuardConditionTo(TState stateFrom, TTrigger trigger, TState stateTo, Func<TState, TTrigger, TState, bool> guardCondition)
        {
            AddGuardConditionTo(_ORguardConditions, stateFrom, trigger, stateTo, guardCondition);

            return this;
        }

        public IFSMWithGuardConditions<TState, TTrigger> RemoveORGuardConditionFrom(TState stateFrom, TTrigger trigger, TState stateTo, Func<TState, TTrigger, TState, bool> guardCondition)
        {
            RemoveGuardConditionFrom(_ORguardConditions, stateFrom, trigger, stateTo, guardCondition);

            return this;
        }


        private void AddGuardConditionTo(Dictionary<FSMTransition<TState, TTrigger>,Func<TState, TTrigger, TState, bool>> guardConditions,
            TState stateFrom, TTrigger trigger, TState stateTo, Func<TState, TTrigger, TState, bool> guardCondition)
        {
            ValidateHasTransition(stateFrom, trigger, stateTo);
            ValidateGuardConditionIsNotNull(guardCondition);

            var comparisonTransition = new FSMTransition<TState, TTrigger>(stateFrom, trigger, stateTo);

            if (guardConditions.ContainsKey(comparisonTransition) == false)
            {
                guardConditions.Add(comparisonTransition, null);
            }

            guardConditions[comparisonTransition] += guardCondition;
        }

        private void RemoveGuardConditionFrom(Dictionary<FSMTransition<TState, TTrigger>, Func<TState, TTrigger, TState, bool>> guardConditions,
            TState stateFrom, TTrigger trigger, TState stateTo, Func<TState, TTrigger, TState, bool> guardCondition)
        {
            ValidateHasTransition(stateFrom, trigger, stateTo);
            ValidateGuardConditionIsNotNull(guardCondition);

            var comparisonTransition = new FSMTransition<TState, TTrigger>(stateFrom, trigger, stateTo);

            if (guardConditions.ContainsKey(comparisonTransition))
            {
                guardConditions[comparisonTransition] -= guardCondition;

                if(guardConditions[comparisonTransition] == null)
                {
                    guardConditions.Remove(comparisonTransition);
                }
            }
        }

        public bool ContainsANDGuardConditionOn(TState stateFrom, TTrigger trigger, TState stateTo, Func<TState, TTrigger, TState, bool> guardCondition)
        {
            return ContainsGuardConditionOn(_ANDguardConditions, stateFrom, trigger, stateTo, guardCondition);
        }

        public bool ContainsORGuardConditionOn(TState stateFrom, TTrigger trigger, TState stateTo, Func<TState, TTrigger, TState, bool> guardCondition)
        {
            return ContainsGuardConditionOn(_ORguardConditions, stateFrom, trigger, stateTo, guardCondition);
        }

        private bool ContainsGuardConditionOn(Dictionary<FSMTransition<TState, TTrigger>, Func<TState, TTrigger, TState, bool>> guardConditions,
            TState stateFrom, TTrigger trigger, TState stateTo, Func<TState, TTrigger, TState, bool> guardCondition)
        {
            var comparisonTransition = new FSMTransition<TState, TTrigger>(stateFrom, trigger, stateTo);

            if (guardConditions.ContainsKey(comparisonTransition))
            {
                return guardConditions[comparisonTransition].GetInvocationList().Contains(guardCondition);
            }

            return false;
        }

        private void ValidateHasTransition(TState stateFrom, TTrigger trigger, TState stateTo)
        {
            if(ContainsTransition(stateFrom, trigger, stateTo) == false)
            {
                throw new TransitionNotAddedException(stateFrom.ToString(), trigger.ToString(), stateTo.ToString());
            }
        }

        private void ValidateGuardConditionIsNotNull(Delegate guardCondition)
        {
            if(guardCondition == null)
            {
                throw new ArgumentNullException("Guard condition was null");
            }
        }
 
        private class FSMStateEqualityComparer : IEqualityComparer<TState>
        {
            public Func<TState, TState, bool> _stateComparer;

            public FSMStateEqualityComparer(Func<TState, TState, bool> stateComparer)
            {
                _stateComparer = stateComparer;
            }

            public bool Equals(TState x, TState y)
            {
                return _stateComparer(x, y);
            }

            public int GetHashCode(TState obj)
            {
                return obj.GetHashCode();
            }
        }

        private struct TransitionRequest
        {
            public TState stateFrom;
            public TTrigger trigger;
        }

        private class FSMTransitionEqualityComparer : IEqualityComparer<FSMTransition<TState, TTrigger>>
        {
            public bool Equals(FSMTransition<TState, TTrigger> x, FSMTransition<TState, TTrigger> y)
            {
                return x == y;
            }

            public int GetHashCode(FSMTransition<TState, TTrigger> obj)
            {
                return obj.GetHashCode();
            }
        }
    }
}
