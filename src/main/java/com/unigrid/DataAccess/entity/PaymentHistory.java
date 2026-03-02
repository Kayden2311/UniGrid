package com.unigrid.DataAccess.entity;

import jakarta.persistence.*;
import lombok.Getter;
import lombok.Setter;
import org.hibernate.annotations.ColumnDefault;
import org.hibernate.annotations.Nationalized;

import java.time.Instant;
import java.util.UUID;

@Getter
@Setter
@Entity
@Table(name = "PaymentHistory", schema = "dbo")
public class PaymentHistory {
    @Id
    @Column(name = "historyId", nullable = false)
    private UUID id;

    @Column(name = "paymentId")
    private UUID paymentId;

    @Column(name = "memberId")
    private UUID memberId;

    @Nationalized
    @Column(name = "\"action\"", length = 100)
    private String action;

    @Nationalized
    @Lob
    @Column(name = "note")
    private String note;

    @ColumnDefault("sysdatetime()")
    @Column(name = "actionTime")
    private Instant actionTime;


}