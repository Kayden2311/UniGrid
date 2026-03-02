package com.unigrid.DataAccess.entity;

import jakarta.persistence.*;
import lombok.Getter;
import lombok.Setter;
import org.hibernate.annotations.ColumnDefault;
import org.hibernate.annotations.Nationalized;

import java.math.BigDecimal;
import java.time.Instant;
import java.util.UUID;

@Getter
@Setter
@Entity
@Table(name = "SubscriptionPlan", schema = "dbo")
public class SubscriptionPlan {
    @Id
    @Column(name = "planId", nullable = false)
    private UUID id;

    @Nationalized
    @Column(name = "name", length = 100)
    private String name;

    @Column(name = "price", precision = 18, scale = 2)
    private BigDecimal price;

    @Column(name = "durationDay")
    private Integer durationDay;

    @Column(name = "maxMember")
    private Integer maxMember;

    @Column(name = "maxStorage")
    private Long maxStorage;

    @ColumnDefault("sysdatetime()")
    @Column(name = "createdAt")
    private Instant createdAt;


}