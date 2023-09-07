using System;

namespace Thea.MessageDriven;

class ProducerInfo
{
    public string ClusterId { get; set; }
    public bool IsUseRpc { get; set; }
    public int ConsumerTotalCount { get; set; }
    public RabbitProducer RabbitProducer { get; set; }
}
class ConsumerInfo
{
    public string ClusterId { get; set; }
    public string ConsumerId { get; set; }
    //public string Queue { get; set; }
    public DateTime UpdatedAt { get; set; }
    public RabbitConsumer RabbitConsumer { get; set; }
}
