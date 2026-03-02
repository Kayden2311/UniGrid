package com.unigrid.DataAccess.entity;

import jakarta.persistence.*;
import lombok.Getter;
import lombok.Setter;
import org.hibernate.annotations.Nationalized;

import java.math.BigDecimal;
import java.time.Instant;
import java.util.UUID;

@Getter
@Setter
@Entity
@Table(name = "Payment", schema = "dbo")
public class Payment {
    @Id
    @Column(name = "paymentId", nullable = false)
    private UUID id;

    @Column(name = "memberId")
    private UUID memberId;

    @Column(name = "subscriptionId")
    private UUID subscriptionId;

    @Nationalized
    @Column(name = "paymentMethod", length = 50)
    private String paymentMethod;

    @Column(name = "paidAt")
    private Instant paidAt;

    @Nationalized
    @Column(name = "paymentStatus", length = 30)
    private String paymentStatus;

    @Column(name = "amount", precision = 18, scale = 2)
    private BigDecimal amount;


}