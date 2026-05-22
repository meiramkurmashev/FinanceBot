using FinanceBot.Models;
using System.Collections.Concurrent;

namespace FinanceBot.Services;

/// <summary>
/// Хранит состояние диалога для каждого пользователя (в памяти).
/// Нужно чтобы реализовать многошаговый диалог:
/// бот спросил "в какую категорию?" → ждёт ответа.
/// </summary>
public class ConversationService
{
    // Ключ = ChatId пользователя, значение = текущее состояние диалога
    private readonly ConcurrentDictionary<long, ConversationState> _states = new();

    public ConversationState Get(long chatId) =>
        _states.GetOrAdd(chatId, _ => new ConversationState());

    public void Set(long chatId, ConversationState state) =>
        _states[chatId] = state;

    public void Reset(long chatId) =>
        _states[chatId] = new ConversationState();

    public bool IsWaiting(long chatId) =>
        Get(chatId).Step != ConversationStep.None;
}
