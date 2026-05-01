using System.Collections.Generic;
using System.Linq;

namespace CardFool
{
    public enum Suits { Hearts, Diamonds, Clubs, Spades };   // черви, бубны, крести, пики

    /// <summary>
    /// Карта
    /// </summary>
    public struct SCard
    {
        public Suits Suit { get; private set; }    // масть, от 0 до 3
        public int Rank { get; private set; }      // величина, от 6 до 14

        public SCard(Suits suit, int rank)
        {
            Suit = suit;
            Rank = rank;
        }
        const string suit = "чбкп";
        const string rank = "-123456789_ВДКТ";
        public override string ToString()
        {
            if (Rank == 0) return "--";
            
            if (Rank == 10)
                return suit[(int)Suit] + "10";

            return suit[(int)Suit].ToString() + rank[Rank];
        }
        public static bool CanBeat(SCard down, SCard up, Suits trump)
        {
            //Если карта козырь, она бьёт любую некозырную
            if (up.Suit == trump && down.Suit != trump)
                return true;

            //Иначе обычные правила:
            return down.Suit == up.Suit && down.Rank < up.Rank;
        }

    }
    /// <summary>
    /// Пара карт на столе
    /// </summary>
    public struct SCardPair
    {
        private SCard _down;    // карта снизу
        private SCard _up;      // карта сверху
        public bool Beaten { get; private set; }   // признак бита карта или нет

        //Получение или установка нижней карты
        public SCard Down
        {
            get { return _down; }
            set { _down = value; Beaten = false; _up = new SCard(); }
        }
        //Верхняя карта
        public SCard Up
        {
            get { return _up; }
        }
        //Установка верхней
        public bool SetUp(SCard up, Suits trump)
        {
            if (!SCard.CanBeat(_down, up, trump))
                return false;

            _up = up;
            Beaten = true;
            return true;
        }
        //Конструктор из нижней карты
        public SCardPair(SCard down)
        {
            _down = down;
            _up = new SCard();
            Beaten = false;
        }
        public override string ToString()
        {
            return _down.ToString() + " vs " + _up.ToString();
        }

        /// <summary>
        /// Преобразование всех карт в список пар с указанной нижней картой
        /// </summary>
        public static List<SCardPair> CardsToCardPairs(List<SCard> cards)
        { 
           return cards.ConvertAll(x => new SCardPair(x));
        }
        //Проверка: "может ли быть карта доброшена к этой паре?"
        public static bool CanBeAddedToPair(SCard newCard, SCardPair pair)
        {
            if (newCard.Rank == pair.Down.Rank)
                return true;
            return pair.Beaten && newCard.Rank == pair.Up.Rank;
        }

    }
    public class MPlayer1
    {

        private List<SCard> LocalGetDeck()
        {
            var deck = new List<SCard>();
            foreach (Suits suit in System.Enum.GetValues(typeof(Suits)))
                for (int rank = 6; rank <= 14; rank++)
                    deck.Add(new SCard(suit, rank));
            return deck;
        }

        private List<SCard> LocalCardPairsToCards(IEnumerable<SCardPair> pairs)
        {
            List<SCard> cards = new List<SCard>();
            foreach (SCardPair pair in pairs)
            {
                cards.Add(pair.Down);
                if (pair.Beaten)
                    cards.Add(pair.Up);
            }
            return cards;
        }

        private string Name = "Dumb Bot";
        private Dictionary<Suits, SortedSet<SCard>> hand = new Dictionary<Suits, SortedSet<SCard>>();
        private SCard trump;

        // Подсчёт карт противника.
        private int opponCards = 6;
        private List<SCardPair> savedTable = new List<SCardPair>();
        private HashSet<SCard> cardsInTable = new HashSet<SCard>();
        private HashSet<SCard> knownOpponentCards = new HashSet<SCard>();
        private HashSet<SCard> destroyedCards = new HashSet<SCard>();
        private bool isDefending = false;

        // Лимит карт, которые можно подкинуть защитнику в текущей атаке
        // Фиксируется на начало атаки и затем только уменьшается
        private int attackLimitForOpponent = 6;

        private int tableCards = 24;
        private bool endGame = false;

        public MPlayer1()
        {
            Reset();
        }

        public string GetName() 
        {
            return Name;
        }
        public int GetCount() 
        { 
            return hand.Values.Sum(set => set.Count);
        }

        private static SortedSet<SCard> NewSuitSet()
        {
            return new SortedSet<SCard>(Comparer<SCard>.Create((a, b) => a.Rank.CompareTo(b.Rank)));
        }

        private static HashSet<SCard> CardsFromTable(IEnumerable<SCardPair> table)
        {
            HashSet<SCard> result = new HashSet<SCard>();
            foreach (SCardPair pair in table)
            {
                result.Add(pair.Down);
                if (pair.Beaten)
                    result.Add(pair.Up);
            }
            return result;
        }

        private int EstimateOpponentCardCount(IEnumerable<SCardPair> currentTable = null)
        {
            var tableCardsNow = currentTable == null
                ? new HashSet<SCard>()
                : CardsFromTable(currentTable);

            int remainingOutsideOurHandAndDiscard =
                LocalGetDeck().Count - GetCount() - destroyedCards.Count - tableCardsNow.Count;

            // В эндгейме прикуп пустой, то бишь все карты вне нашей руки, стола и сброса обязаны быть в руке противника
            if (endGame || tableCards <= 0)
                return System.Math.Max(0, remainingOutsideOurHandAndDiscard);

            HashSet<SCard> possibleOpponentOrDeck = new HashSet<SCard>(cardsInTable);
            possibleOpponentOrDeck.UnionWith(knownOpponentCards);
            possibleOpponentOrDeck.ExceptWith(tableCardsNow);

            int estimate = System.Math.Min(possibleOpponentOrDeck.Count, remainingOutsideOurHandAndDiscard);
            return System.Math.Max(0, System.Math.Min(6, estimate));
        }

        private void UpdateOpponentCardCount(IEnumerable<SCardPair> currentTable = null)
        {
            opponCards = EstimateOpponentCardCount(currentTable);
        }


        private int initialCardsReceived = 0;

        private void RefreshEndGame(IEnumerable<SCardPair> currentTable = null)
        {
            if (endGame)
            {
                LearnAllRemainingOpponentCards(currentTable);
                return;
            }

            if (tableCards <= 0)
            {
                endGame = true;
                tableCards = 0;
                LearnAllRemainingOpponentCards(currentTable);
            }
        }

        private void LearnAllRemainingOpponentCards(IEnumerable<SCardPair> currentTable = null)
        {
            HashSet<SCard> tableCardsNow = currentTable == null
                ? new HashSet<SCard>()
                : CardsFromTable(currentTable);

            // При пустом прикупе руку противника считаем не из cardsInTable, а как: не у нас, не на столе, не в сбросе
            // Другой вариант оставлял в knownOpponentCards лишние карты и завышал лимит атаки
            var opponent = LocalGetDeck().ToHashSet();
            opponent.ExceptWith(tableCardsNow);
            opponent.ExceptWith(destroyedCards);
            foreach (var c in hand.SelectMany(kv => kv.Value))
                opponent.Remove(c);

            knownOpponentCards = opponent;
            opponCards = opponent.Count;
            attackLimitForOpponent = System.Math.Min(attackLimitForOpponent, System.Math.Min(6, opponCards));
        }

        private int CheapestOpponentAnswerRank(SCard card, IEnumerable<SCard> opponentCards)
        {
            SCard? answer = opponentCards
                .Where(enemy => SCard.CanBeat(card, enemy, trump.Suit))
                .Cast<SCard?>()
                .OrderBy(enemy => enemy!.Value.Suit == trump.Suit ? 1 : 0)
                .ThenBy(enemy => enemy!.Value.Rank)
                .FirstOrDefault();

            return answer == null ? 100 : answer.Value.Rank;
        }

        private int EndGameAttackScore(SCard card)
        {
            int answerRank = CheapestOpponentAnswerRank(card, knownOpponentCards);
            int unbeatableBonus = answerRank == 100 ? -1000 : 0;
            int trumpPenalty = card.Suit == trump.Suit ? 100 : 0;
            return unbeatableBonus + trumpPenalty - answerRank + card.Rank;
        }

        private List<SCard> RealLayCardsEndGame()
        {
            RefreshEndGame();
            int maxStartCards = System.Math.Min(6, opponCards);
            if (maxStartCards <= 0)
                return new List<SCard>();

            SCard? bestFirst = hand
                .SelectMany(kv => kv.Value)
                .Cast<SCard?>()
                .OrderBy(c => EndGameAttackScore(c!.Value))
                .ThenBy(c => c!.Value.Suit == trump.Suit ? 1 : 0)
                .ThenBy(c => c!.Value.Rank)
                .FirstOrDefault();

            if (bestFirst == null)
                return new List<SCard>();

            int rankToLay = bestFirst.Value.Rank;
            List<SCard> cards = hand
                .SelectMany(kv => kv.Value)
                .Where(c => c.Rank == rankToLay)
                .OrderBy(c => EndGameAttackScore(c))
                .Take(maxStartCards)
                .ToList();

            foreach (SCard card in cards)
            {
                hand[card.Suit].Remove(card);
                if (hand[card.Suit].Count == 0)
                    hand.Remove(card.Suit);
            }

            return cards;
        }

        private bool RealAddCardsEndGame(List<SCardPair> table)
        {
            RefreshEndGame(table);
            int canThrowMax = System.Math.Min(6, attackLimitForOpponent) - table.Count;
            if (canThrowMax <= 0) return false;

            HashSet<int> ranks = new HashSet<int>();
            foreach (SCardPair pair in table)
            {
                ranks.Add(pair.Down.Rank);
                if (pair.Beaten)
                    ranks.Add(pair.Up.Rank);
            }

            var candidates = hand
                .SelectMany(kv => kv.Value)
                .Where(c => ranks.Contains(c.Rank))
                .OrderBy(c => EndGameAttackScore(c))
                .ThenBy(c => c.Suit == trump.Suit ? 1 : 0)
                .ThenBy(c => c.Rank)
                .Take(canThrowMax)
                .ToList();

            foreach (SCard card in candidates)
            {
                table.Add(new SCardPair(card));
                hand[card.Suit].Remove(card);
                if (hand[card.Suit].Count == 0)
                    hand.Remove(card.Suit);
            }

            return candidates.Count > 0;
        }

        public void AddToHand(SCard card)
        {
            cardsInTable.Remove(card);
            knownOpponentCards.Remove(card);

            if (initialCardsReceived < 6)
                initialCardsReceived++;
            else if (tableCards > 0)
                tableCards--;

            RefreshEndGame();

            if (!hand.ContainsKey(card.Suit))
                hand[card.Suit] = NewSuitSet();

            hand[card.Suit].Add(card);
        }

        public List<SCard> LayCards()
        {
            if (isDefending)
            {
                // Если мы отбились и теперь атакуем
                destroyedCards.UnionWith(LocalCardPairsToCards(savedTable.Where(x => x.Beaten)));
                cardsInTable.RemoveWhere(x => savedTable.Any(y => y.Down.Equals(x)));

                // Карты, от которых мы отбились, уже не у противника
                knownOpponentCards.RemoveWhere(x => savedTable.Any(y => y.Down.Equals(x) && y.Beaten));

                // Небитые карты вернулись противнику в руку
                knownOpponentCards.UnionWith(savedTable.Where(y => !y.Beaten).Select(x => x.Down));
            }
            else
            {
                // Если противник взял карты со стола
                cardsInTable.RemoveWhere(x => savedTable.Any(y => y.Up.Equals(x)));
                knownOpponentCards.UnionWith(LocalCardPairsToCards(savedTable));
            }

            isDefending = false;
            UpdateOpponentCardCount();
            attackLimitForOpponent = opponCards;
            RefreshEndGame();

            if (endGame)
                return RealLayCardsEndGame();

            // Примитивная, но сильная-классическая атака начинаем с минимального ранга и сразу выкладываем все карты этого ранга, сколько защитник может принять
            int maxStartCards = System.Math.Min(6, attackLimitForOpponent);
            if (maxStartCards <= 0)
                return new List<SCard>();

            SCard? firstCard = hand
                .SelectMany(kv => kv.Value)
                .Where(c => c.Suit != trump.Suit)
                .Cast<SCard?>()
                .OrderBy(c => c!.Value.Rank)
                .ThenBy(c => c!.Value.Suit)
                .FirstOrDefault();

            if (firstCard == null)
            {
                firstCard = hand
                    .SelectMany(kv => kv.Value)
                    .Cast<SCard?>()
                    .OrderBy(c => c!.Value.Rank)
                    .ThenBy(c => c!.Value.Suit)
                    .FirstOrDefault();
            }

            if (firstCard == null)
                return new List<SCard>();

            int rankToLay = firstCard.Value.Rank;
            List<SCard> cards = hand
                .SelectMany(kv => kv.Value)
                .Where(c => c.Rank == rankToLay)
                .OrderBy(c => c.Suit == trump.Suit ? 1 : 0)
                .ThenBy(c => c.Rank)
                .Take(maxStartCards)
                .ToList();

            foreach (SCard card in cards)
            {
                hand[card.Suit].Remove(card);
                if (hand[card.Suit].Count == 0)
                    hand.Remove(card.Suit);
            }

            return cards;
        }

        public bool Defend(List<SCardPair> table)
        {
            if (!isDefending)
            {
                // Если противник отбился, битые пары ушли
                destroyedCards.UnionWith(LocalCardPairsToCards(savedTable.Where(x => x.Beaten)));
                knownOpponentCards.RemoveWhere(x => savedTable.Any(y => y.Up.Equals(x)));
                cardsInTable.RemoveWhere(x => savedTable.Any(y => y.Up.Equals(x)));
            }

            isDefending = true;
            bool defended = RealDefend(table);
            savedTable = table.ToList();
            return defended;
        }

        private int DefenceCardCost(SCard attack, SCard defence, Dictionary<int, int> rankCountsBefore)
        {
            int cost = defence.Rank;

            // Проще не жечь козыри, если есть обычная защита
            if (defence.Suit == trump.Suit && attack.Suit != trump.Suit)
                cost += 10000;
            else if (defence.Suit == trump.Suit)
                cost += 2000;

            // Низкие некозырные карты хороши для следующей атаки, поэтому тратить их чуть хуже
            if (defence.Suit != trump.Suit && defence.Rank <= 10)
                cost += 20 - defence.Rank;

            // Если это последняя карта данного ранга, мы теряем один возможный атакующий ранг
            if (rankCountsBefore.TryGetValue(defence.Rank, out int count) && count == 1)
                cost += 8;

            return cost;
        }

        private int FutureHandScoreAfterDefence(HashSet<SCard> used)
        {
            var remaining = hand.SelectMany(kv => kv.Value).Where(c => !used.Contains(c)).ToList();
            int score = 0;

            foreach (var group in remaining.GroupBy(c => c.Rank))
            {
                // Пачка одного ранга — сильный первый ход
                score += group.Count() * group.Count() * 25;

                // Низкие ранги выгоднее отдавать в атаку.
                score += System.Math.Max(0, 15 - group.Key);
            }

            // Козыри в эндгейме особенно ценны в защите
            score += remaining.Count(c => c.Suit == trump.Suit) * 60;
            return score;
        }

        private bool RealDefendEndGame(List<SCardPair> table)
        {
            RefreshEndGame(table);

            List<int> unbeatenIndexes = table
                .Select((pair, index) => new { pair, index })
                .Where(x => !x.pair.Beaten)
                .Select(x => x.index)
                .ToList();

            if (unbeatenIndexes.Count == 0)
                return true;

            var allHandCards = hand.SelectMany(kv => kv.Value).ToList();
            var rankCountsBefore = allHandCards
                .GroupBy(c => c.Rank)
                .ToDictionary(g => g.Key, g => g.Count());

            List<SCard> bestDefence = null;
            int bestCost = int.MaxValue;

            void Search(int pos, HashSet<SCard> used, List<SCard> chosen, int costSoFar)
            {
                if (costSoFar >= bestCost)
                    return;

                if (pos >= unbeatenIndexes.Count)
                {
                    int futureBonus = FutureHandScoreAfterDefence(used);
                    int totalCost = costSoFar - futureBonus;
                    if (totalCost < bestCost)
                    {
                        bestCost = totalCost;
                        bestDefence = chosen.ToList();
                    }
                    return;
                }

                int tableIndex = unbeatenIndexes[pos];
                SCard attack = table[tableIndex].Down;

                var candidates = allHandCards
                    .Where(c => !used.Contains(c) && SCard.CanBeat(attack, c, trump.Suit))
                    .OrderBy(c => DefenceCardCost(attack, c, rankCountsBefore))
                    .ThenBy(c => c.Suit == trump.Suit ? 1 : 0)
                    .ThenBy(c => c.Rank)
                    .ToList();

                foreach (var card in candidates)
                {
                    used.Add(card);
                    chosen.Add(card);
                    Search(pos + 1, used, chosen, costSoFar + DefenceCardCost(attack, card, rankCountsBefore));
                    chosen.RemoveAt(chosen.Count - 1);
                    used.Remove(card);
                }
            }

            Search(0, new HashSet<SCard>(), new List<SCard>(), 0);

            if (bestDefence == null)
                return false;

            for (int i = 0; i < unbeatenIndexes.Count; i++)
            {
                int tableIndex = unbeatenIndexes[i];
                SCard card = bestDefence[i];

                var pair = table[tableIndex];
                pair.SetUp(card, trump.Suit);
                table[tableIndex] = pair;

                hand[card.Suit].Remove(card);
                if (hand[card.Suit].Count == 0)
                    hand.Remove(card.Suit);
            }

            return true;
        }

        private bool RealDefend(List<SCardPair> table)
        {
            if (endGame || tableCards <= 0)
                return RealDefendEndGame(table);

            for (int i = 0; i < table.Count; i++)
            {
                if (table[i].Beaten) continue;

                SCard? found = null;

                if (hand.TryGetValue(table[i].Down.Suit, out var sameSuit))
                    found = sameSuit
                        .Cast<SCard?>()
                        .FirstOrDefault(c => SCard.CanBeat(table[i].Down, c!.Value, trump.Suit));

                if (found == null && hand.TryGetValue(trump.Suit, out var trumps))
                    found = trumps
                        .Cast<SCard?>()
                        .FirstOrDefault(c => SCard.CanBeat(table[i].Down, c!.Value, trump.Suit));

                if (found == null) return false;

                var pair = table[i];
                pair.SetUp(found.Value, trump.Suit);
                table[i] = pair;

                hand[found.Value.Suit].Remove(found.Value);
                if (hand[found.Value.Suit].Count == 0)
                    hand.Remove(found.Value.Suit);
            }
            return true;
        }

        public bool AddCards(List<SCardPair> table, bool OpponentDefenced)
        {
            // Лимит нельзя пересчитывать обратно до 6 во время атаки
            // Если по текущему столу видно, что у защитника осталось меньше карт, только сужаем лимит
            int remainingOpponentCards = EstimateOpponentCardCount(table);
            // Нельзя сделать на столе больше нижних карт, чем защитник мог принять с учётом его текущей руки
            attackLimitForOpponent = System.Math.Min(attackLimitForOpponent, table.Count + remainingOpponentCards);
            opponCards = remainingOpponentCards;
            RefreshEndGame(table);

            bool added = endGame ? RealAddCardsEndGame(table) : RealAddCards(table);
            savedTable = table.ToList();
            return added;
        }

        private bool RealAddCards(List<SCardPair> table)
        {
            int canThrowMax = System.Math.Min(6, attackLimitForOpponent) - table.Count;
            if (canThrowMax <= 0) return false;

            HashSet<int> ranks = new HashSet<int>();
            foreach (SCardPair pair in table)
            {
                ranks.Add(pair.Down.Rank);
                if (pair.Beaten)
                    ranks.Add(pair.Up.Rank);
            }

            int addedCards = 0;
            foreach (Suits suit in hand.Keys.ToList())
            {
                foreach (SCard card in hand[suit].ToList())
                {
                    if (!ranks.Contains(card.Rank)) continue;

                    table.Add(new SCardPair(card));
                    addedCards++;
                    hand[suit].Remove(card);
                    if (hand[suit].Count == 0)
                        hand.Remove(suit);

                    if (addedCards >= canThrowMax) return true;
                }
            }

            return addedCards > 0;
        }

        public void OnEndRound(List<SCardPair> table, bool IsDefenceSuccesful)
        {
            savedTable = table.ToList();

            if (IsDefenceSuccesful)
            {
                destroyedCards.UnionWith(LocalCardPairsToCards(table));
                knownOpponentCards.RemoveWhere(x => table.Any(y => y.Down.Equals(x) || (y.Beaten && y.Up.Equals(x))));
                cardsInTable.RemoveWhere(x => table.Any(y => y.Down.Equals(x) || (y.Beaten && y.Up.Equals(x))));
            }
            else
            {
                knownOpponentCards.UnionWith(LocalCardPairsToCards(table));
                cardsInTable.RemoveWhere(x => table.Any(y => y.Down.Equals(x) || (y.Beaten && y.Up.Equals(x))));
            }

            if (tableCards > 0)
            {
                int beforeCap = System.Math.Min(6, knownOpponentCards.Count + cardsInTable.Count);
                int opponentNeed = System.Math.Max(0, 6 - beforeCap);
                tableCards = System.Math.Max(0, tableCards - opponentNeed);
            }

            UpdateOpponentCardCount();
            attackLimitForOpponent = opponCards;
            RefreshEndGame();
        }

        public void SetTrump(SCard NewTrump)
        {
            trump = NewTrump;
        }

        public void Reset()
        {
            hand = new Dictionary<Suits, SortedSet<SCard>>();
            opponCards = 6;
            cardsInTable = LocalGetDeck().ToHashSet();
            knownOpponentCards = new HashSet<SCard>();
            savedTable = new List<SCardPair>();
            destroyedCards = new HashSet<SCard>();
            isDefending = false;
            attackLimitForOpponent = 6;
            tableCards = 24;
            endGame = false;
            initialCardsReceived = 0;
        }
    }
}
