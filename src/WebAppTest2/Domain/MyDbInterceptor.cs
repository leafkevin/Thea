using System;
using System.Threading;
using Trolley;

namespace WebAppTest2.Domain;

public class MyDbInterceptor : DefautDbInterceptor
{
    private static int connTotal = 0;
    private static int connOpenTotal = 0;
    private static int commandTotal = 0;
    private static int readerTotal = 0;
    private static int tranTotal = 0;

    public override ITheaConnection ConnectionCreated(ITheaConnection connection)
    {
        Interlocked.Increment(ref connTotal);
        Console.WriteLine($"Connection {connection.ConnectionId} created, Total:{Volatile.Read(ref connTotal)}");
        return connection;
    }
    public override void ConnectionOpened(ITheaConnection connection)
    {
        Interlocked.Increment(ref connOpenTotal);
        Console.WriteLine($"Connection {connection.ConnectionId} opened, Total:{Volatile.Read(ref connOpenTotal)}");
    }
    public override void ConnectionClosed(ITheaConnection connection)
    {
        Interlocked.Decrement(ref connOpenTotal);
        Interlocked.Decrement(ref connTotal);
        Console.WriteLine($"Connection {connection.ConnectionId} closed, Total:{Volatile.Read(ref connOpenTotal)}");
    }
    public override ITheaCommand CommandCreated(ITheaCommand command)
    {
        Interlocked.Increment(ref commandTotal);
        //Console.WriteLine($"Command CommandId:{command.CommandId} created, Total:{Volatile.Read(ref commandTotal)}");
        Console.WriteLine($"Command CommandId:{command.CommandId} created, Total:{Volatile.Read(ref commandTotal)}");
        return command;
    }
    public override DbCommandExecutingEventArgs CommandExecuting(ITheaCommand command)
    {
        Console.WriteLine($"Begin, CommandId:{command.CommandId} Sql: {command.CommandText}, Parameters: {command.Parameters.ToMySqlParametersString()}");
        return new DbCommandExecutingEventArgs { IsCanExecuting = true, Command = command, EventData = DateTime.UtcNow };
    }
    public override void CommandExecuted(DbCommandCompletedEventArgs eventArgs)
    {
        var command = eventArgs.Command;
        var startTime = (DateTime)eventArgs.EventData;
        var elapsed = DateTime.UtcNow.Subtract(startTime).TotalMilliseconds;
        Console.WriteLine($"End, CommandId:{command.CommandId} Elapsed: {elapsed} ms");
    }
    public override void CommandDisposed(ITheaCommand command)
    {
        Interlocked.Decrement(ref commandTotal);
        //Console.WriteLine($"Command {command.CommandId} disposed, Total:{Volatile.Read(ref commandTotal)}");
        Console.WriteLine($"Command {command.CommandId} disposed, Total:{Volatile.Read(ref commandTotal)}");
    }
    public override ITheaDataReader DataReaderCreated(ITheaDataReader reader)
    {
        Interlocked.Increment(ref readerTotal);
        Console.WriteLine($"DataReder {reader.ReaderId} created, CommandId: {reader.CommandId}, Total:{Volatile.Read(ref readerTotal)}");
        return reader;
    }
    public override void DataReaderDisposed(ITheaDataReader reader)
    {
        Interlocked.Decrement(ref readerTotal);
        Console.WriteLine($"DataReder {reader.ReaderId} disposed, CommandId: {reader.CommandId}, Total:{Volatile.Read(ref readerTotal)}");
    }
    public override ITheaTransaction TransactionCreated(ITheaTransaction transaction)
    {
        Interlocked.Increment(ref tranTotal);
        Console.WriteLine($"Transaction {transaction.TransactionId} created, Total:{Volatile.Read(ref tranTotal)}");
        return transaction;
    }
    public override void TransactionCommitted(DbTransactionCompletedEventArgs eventArgs)
    {
        var transaction = eventArgs.Transaction;
        var startTime = (DateTime)eventArgs.EventData;
        var elapsed = DateTime.UtcNow.Subtract(startTime).TotalMilliseconds;
        Console.WriteLine($"Commit completed, Total:{Volatile.Read(ref tranTotal)}, TransactionId:{transaction.TransactionId}, Elapsed: {elapsed} ms");
    }
    public override void TransactionFailed(DbTransactionCompletedEventArgs eventArgs)
    {
        var transaction = eventArgs.Transaction;
        var startTime = (DateTime)eventArgs.EventData;
        var elapsed = DateTime.UtcNow.Subtract(startTime).TotalMilliseconds;
        Console.WriteLine($"Rollback completed, Total:{Volatile.Read(ref tranTotal)}, TransactionId:{transaction.TransactionId}, Elapsed: {elapsed} ms");
    }
}