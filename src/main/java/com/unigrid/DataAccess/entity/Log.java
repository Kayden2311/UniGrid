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
@Table(name = "Log", schema = "dbo")
public class Log {
    @Id
    @Column(name = "logId", nullable = false)
    private UUID id;

    @Column(name = "adminId", nullable = false)
    private UUID adminId;

    @Nationalized
    @Column(name = "\"action\"", nullable = false)
    private String action;

    @ColumnDefault("sysdatetime()")
    @Column(name = "createdAt", nullable = false)
    private Instant createdAt;


}