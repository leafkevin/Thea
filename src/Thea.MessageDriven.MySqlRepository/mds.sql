-- -------------- TABLE [mds_binding] BEGIN----------------
-- DROP TABLE IF EXISTS `mds_binding`;
CREATE TABLE `mds_binding`
(
    `exchange_id` VARCHAR(50) NOT NULL COMMENT '信箱ID',
    `queue_id` VARCHAR(50) NOT NULL COMMENT '队列ID',
    `bind_type` VARCHAR(50) NULL COMMENT '绑定类型',
    `binding_key` VARCHAR(50) NULL COMMENT '绑定KEY',
    `is_need_transfer` TINYINT(1) NULL DEFAULT 0 COMMENT '是否需要转发',
    `is_delay` TINYINT(1) NULL DEFAULT 0 COMMENT '是否延时消费者',
    CONSTRAINT `pk_mds_binding` PRIMARY KEY(`exchange_id`,`queue_id`)
);
ALTER TABLE `mds_binding` COMMENT '绑定表，描述交换机与队列的绑定关系';
-- -------------- TABLE [mds_binding] END----------------



-- -------------- TABLE [mds_queue] BEGIN----------------
-- DROP TABLE IF EXISTS `mds_queue`;
CREATE TABLE `mds_queue`
(
    `queue_id` VARCHAR(50) NOT NULL COMMENT '队列ID',
    `queue_name` VARCHAR(50) NULL COMMENT '队列名称',
    `is_stateful` TINYINT(1) NULL DEFAULT 0 COMMENT '是否有状态',
    `workload_total` INTEGER NULL COMMENT '工作负荷个数',
    `is_sac` TINYINT(1) NULL DEFAULT 0 COMMENT '是否单一激活消费者',
    `prefetch_count` INTEGER NULL COMMENT '预取个数',
    `is_log_enabled` TINYINT(1) NULL DEFAULT 0 COMMENT '是否开启日志',
    `is_enabled` TINYINT(1) NOT NULL DEFAULT 1 COMMENT '是否启用',
    CONSTRAINT `pk_mds_queue` PRIMARY KEY(`queue_id`)

);
ALTER TABLE `mds_queue` COMMENT '队列表，描述所有的队列基本信息';
-- -------------- TABLE [mds_queue] END----------------



-- -------------- TABLE [mds_log] BEGIN----------------
-- DROP TABLE IF EXISTS `mds_log`;
CREATE TABLE `mds_log`
(
    `log_id` VARCHAR(50) NOT NULL COMMENT '日志ID',
    `exchange_id` VARCHAR(50) NULL COMMENT '信箱ID',
    `routing_key` VARCHAR(50) NULL COMMENT '路由KEY',
    `queue` VARCHAR(50) NULL COMMENT '队列名称',
    `body` VARCHAR(4000) NULL COMMENT '消息内容',
    `is_success` TINYINT(1) NULL DEFAULT 0 COMMENT '是否成功',
    `result` VARCHAR(4000) NULL COMMENT '执行结果',
    `retry_times` INTEGER NULL COMMENT '重试次数',
    `updated_at` DATETIME NOT NULL DEFAULT NOW() COMMENT '最后更新日期',
    CONSTRAINT `pk_mds_log` PRIMARY KEY(`log_id`)
);
ALTER TABLE `mds_log` COMMENT '日志表，描述消息队列每个消费者的执行日志';
-- -------------- TABLE [mds_log] END----------------
